/*!
 * @author electricessence / https://github.com/electricessence/
 * Licensing: MIT https://github.com/electricessence/Open/blob/dotnet-core/LICENSE.md
 */

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Open.Diagnostics;
using Open.Disposable;

namespace Open.Threading
{
	/// <summary>
	/// Utility class for synchronizing read write access to different resources in the same domain/scope.
	/// This essentially has it's own garbage collector to prevent building up memory/references to unused locks.
	/// </summary>
	public class ReadWriteHelper<TKey> : DeferredCleanupBase
	{

		private class ReaderWriterLockTracker : DisposableBase
		{
			readonly List<object> _registry = new List<object>();

			internal ReaderWriterLockSlim Lock;

			public ReaderWriterLockTracker(ReaderWriterLockSlim rwlock)
				: base()
			{
				Lock = rwlock;
			}

			public ReaderWriterLockTracker(LockRecursionPolicy policy) : this(new ReaderWriterLockSlim(policy))
			{
			}

			public bool Reserve(object context)
			{
				if (IsDisposed)
					return false;

				lock (_registry)
					_registry.Add(context);

				return true;
			}

			public void Clear(object context)
			{
				lock (_registry)
					_registry.Remove(context);
			}

			public bool CanDispose
			{
				get
				{
					lock (_registry)
						return !_registry.Any();
				}
			}

			protected override void OnDispose(bool calledExplicitly)
			{
				lock (_registry)
				{
					//Contract.Assume(CanDispose);
					var count = _registry.Count;
					Debug.WriteLineIf(count != 0, "Disposing a ReaderWriterLockTracker with " + count + " contexts still registered.");
					_registry.Clear();
				}

				var l = Lock;
				Lock = null;
				l?.Dispose();
			}
		}


		readonly ConcurrentBag<object> ContextPool = new ConcurrentBag<object>();
		readonly ConcurrentBag<ReaderWriterLockTracker> LockPool = new ConcurrentBag<ReaderWriterLockTracker>();

		readonly ConcurrentDictionary<TKey, ReaderWriterLockTracker> Locks
			= new ConcurrentDictionary<TKey, ReaderWriterLockTracker>();

		readonly ReaderWriterLockSlim CleanupManager
			= new ReaderWriterLockSlim(LockRecursionPolicy.SupportsRecursion);

		public ReadWriteHelper() : this(false)
		{

		}

		public ReadWriteHelper(bool supportRecursion)
		{
			//Debug.WriteLine("Constructing: "+this.ToString());

			RecursionPolicy = supportRecursion
				? LockRecursionPolicy.SupportsRecursion
				: LockRecursionPolicy.NoRecursion;
		}

		public LockRecursionPolicy RecursionPolicy
		{
			get;
			private set;
		}

		private ReaderWriterLockTracker GetLock(TKey key, LockType type, object context, int? millisecondsTimeout = null, bool throwsOnTimeout = false)
		{
			if (key == null)
				throw new ArgumentNullException("key");
			if (context == null)
				throw new ArgumentNullException("context");
			ReaderWriterLockSlimExensions.ValidateMillisecondsTimeout(millisecondsTimeout);

			if (IsDisposed)
				return null;

			// Need to be able to enter a lock before releasing access in order to prevent removal...
			var r = CleanupManager.ReadValue(() =>
		   	{
				   // It is possible that a read could be acquired while disposing just before the dispose.
				   if (IsDisposed)
					   return null;

				   // Get a tracker...
				   ReaderWriterLockTracker result;
				   {
					   // Compare the tracker retrieved with the one created...
					   ReaderWriterLockTracker created = null;
					   do
					   {
						   result = Locks.GetOrAdd(key, k =>
						   {
							   if (!LockPool.TryTake(out created))
							   {
								   created = new ReaderWriterLockTracker(RecursionPolicy);
								   if (Debugger.IsAttached)
									   created.BeforeDispose += new EventHandler(Debug_TrackerDisposedWhileInUse);
							   }
							   return created;
						   });
					   }
					   // Safeguard against rare case of when a disposed tracker is retained via an exception (possibly?). :(
					   while (!IsDisposed && result.IsDisposed);


					   // If the one created is not the one retrieved, go ahead and add it to the pool so it doesn't go to waste.
					   if (created != null && created != result)
					   {
						   if (IsDisposed)
							   created.Dispose();
						   else
							   LockPool.Add(created);
					   }

					   // This should never get out of sync, but just in case...
					   var rlock = result.Lock;
					   if (rlock == null || result.IsDisposed)
					   {
						   Debug.Fail("A lock tracker was retained after it was disposed.");
						   return null;
					   }
					   else if (Debugger.IsAttached && rlock.RecursionPolicy == LockRecursionPolicy.NoRecursion)
					   {
						   if (rlock.IsWriteLockHeld && type == LockType.Read)
							   Debugger.Break(); // 
					   }
				   }

				   // Quick check to avoid further processes...
				   if (IsDisposed)
					   return null;

				   bool lockHeld = false;
				   if (result.Reserve(context)) // Rare synchronization instance where this may be disposing at this point.
				   {
					   try
					   {
						   // result.Lock will only be null if the tracker has been disposed.
						   lockHeld = AcquireLock(result.Lock, type, millisecondsTimeout, throwsOnTimeout);
					   }
					   catch (LockRecursionException lrex)
					   {
						   lrex.WriteToDebug();
						   Debugger.Break(); // Need to be able to track down source.
						   throw;
					   }
					   finally
					   {
						   if (!lockHeld)
							   result.Clear(context);
					   }
				   }

				   if (lockHeld)
					   return result;
				   else
					   return null; // Null indicates a lock could not be acquired...
			   });

			// In the rare case that a dispose could be initiated during this ReadValue:
			// We need to not propagate locking...
			if (r != null && IsDisposed)
			{
				ReleaseLock(r.Lock, type);
				r.Clear(context);
				r = null;
			}

			return r;

		}

		void Debug_TrackerDisposedWhileInUse(object sender, EventArgs e)
		{
			var tracker = (ReaderWriterLockTracker)sender;
			if (Locks.Select(kvp=>kvp.Value).Contains(tracker))
				Debug.Fail("Attempting to dispose a tracker that is in use.");
			if (LockPool.Contains(tracker))
				Debug.Fail("Attempting to dispose a tracker that is still availalbe in the pool.");
		}

		private bool AcquireLock(ReaderWriterLockSlim target, LockType type, int? millisecondsTimeout = null, bool throwsOnTimeout = false)
		{
			if (target == null)
				throw new ArgumentNullException("target");
			ReaderWriterLockSlimExensions.ValidateMillisecondsTimeout(millisecondsTimeout);

			switch (type)
			{
				case LockType.Read:
					return target.EnterReadLock(millisecondsTimeout, throwsOnTimeout);
				case LockType.ReadUpgradeable:
					return target.EnterUpgradeableReadLock(millisecondsTimeout, throwsOnTimeout);
				case LockType.Write:
					return target.EnterWriteLock(millisecondsTimeout, throwsOnTimeout);
			}

			return false;
		}

		private void ReleaseLock(ReaderWriterLockSlim target, LockType type)
		{
			if (target != null)
			{
				switch (type)
				{
					case LockType.Read:
						target.ExitReadLock();
						break;
					case LockType.ReadUpgradeable:
						target.ExitUpgradeableReadLock();
						break;
					case LockType.Write:
						target.ExitWriteLock();
						break;
				}

				if (!IsDisposed)
				{
					UpdateCleanupDelay();

					//SetCleanup(CleanupMode.ImmediateSynchronous); 
					// Now that we've realeased the lock, signal for cleanup later...
					SetCleanup(CleanupMode.ImmediateDeferredIfPastDue);
				}
			}
		}


		// Funnel all delegates through here to ensure proper procedure for getting and releasing locks.
		private bool Execute(TKey key, LockType type, Action<ReaderWriterLockSlim> closure, int? millisecondsTimeout = null, bool throwsOnTimeout = false)
		{
			if (key == null)
				throw new ArgumentNullException("key");
			if (closure == null)
				throw new ArgumentNullException("closure");
			ReaderWriterLockSlimExensions.ValidateMillisecondsTimeout(millisecondsTimeout);

            if (!ContextPool.TryTake(out object context) || context == null)
                context = new Object();

            ReaderWriterLockTracker rwlock = GetLock(key, type, context, millisecondsTimeout, throwsOnTimeout);
			if (rwlock == null)
				return false;

			try
			{
				closure(rwlock.Lock);
			}
			finally
			{
				try
				{
					ReleaseLock(rwlock.Lock, type);
					rwlock.Clear(context);
					ContextPool.Add(context);
				}
				catch (Exception ex)
				{
					ex.WriteToDebug();
					// The above cannot fail or dire concequences...
					Debugger.Break();
					throw;
				}
			}
			return true;
		}

		private bool Execute<T>(TKey key, LockType type, out T result, Func<ReaderWriterLockSlim, T> closure, int? millisecondsTimeout = null, bool throwsOnTimeout = false)
		{
			T r = default(T);

			bool acquired = Execute(key, type, (rwlock) =>
			{
				r = closure(rwlock);
			}, millisecondsTimeout, throwsOnTimeout);

			result = r;

			return acquired;
		}

		// Funnel all delegates through here to ensure proper procedure for getting and releasing locks.
		private bool Execute(TKey key, LockType type, Action closure, int? millisecondsTimeout = null, bool throwsOnTimeout = false)
		{
			if (closure == null)
				throw new ArgumentNullException("closure");
			return Execute(key, type, (rwlock) => closure(), millisecondsTimeout, throwsOnTimeout);
		}

		// Funnel all delegates through here to ensure proper procedure for getting and releasing locks.
		private bool Execute<T>(TKey key, LockType type, out T result, Func<T> closure, int? millisecondsTimeout = null, bool throwsOnTimeout = false)
		{
			if (closure == null)
				throw new ArgumentNullException("closure");
			return Execute(key, type, out result, (rwlock) => closure(), millisecondsTimeout, throwsOnTimeout);
		}

		#region Read/Write Public Interface
		/// <summary>
		/// Executes the query within a read lock based upon the cacheKey provided..
		/// </summary>
		/// <returns>Returns false if a timeout is reached.</returns>
		public bool Read(
			TKey key, Action closure,
			int? millisecondsTimeout = null, bool throwsOnTimeout = false)
		{
			return Execute(key, LockType.Read, closure, millisecondsTimeout, throwsOnTimeout);
		}

		/// <summary>
		/// Executes the query within a read lock based upon the cacheKey provided..
		/// </summary>
		/// <returns>Returns false if a timeout is reached.</returns>
		public bool Read<T>(
			TKey key, out T result, Func<T> valueFactory,
			int? millisecondsTimeout = null, bool throwsOnTimeout = false)
		{
			return Execute(key, LockType.Read, out result, valueFactory, millisecondsTimeout, throwsOnTimeout);
		}

		/// <summary>
		/// Executes the query within an upgradeable read lock based upon the cacheKey provided..
		/// </summary>
		/// <returns>Returns false if a timeout is reached.</returns>
		private bool ReadUpgradeable(
			TKey key, Action<ReaderWriterLockSlim> closure,
			int? millisecondsTimeout = null, bool throwsOnTimeout = false)
		{
			return Execute(key, LockType.ReadUpgradeable, closure, millisecondsTimeout, throwsOnTimeout);
		}

		/// <summary>
		/// Executes the query within an upgradeable read lock based upon the cacheKey provided..
		/// </summary>
		/// <returns>Returns false if a timeout is reached.</returns>
		public bool ReadUpgradeable<T>(
			TKey key, out T result, Func<T> closure,
			int? millisecondsTimeout = null, bool throwsOnTimeout = false)
		{
			return Execute(key, LockType.ReadUpgradeable, out result, closure, millisecondsTimeout, throwsOnTimeout);
		}

		/// <summary>
		/// Executes the query within an upgradeable read lock based upon the cacheKey provided..
		/// </summary>
		/// <returns>Returns false if a timeout is reached.</returns>
		public bool ReadUpgradeable(
			TKey key, Action closure,
			int? millisecondsTimeout = null, bool throwsOnTimeout = false)
		{
			return Execute(key, LockType.ReadUpgradeable, closure, millisecondsTimeout, throwsOnTimeout);
		}

		/// <summary>
		/// Executes the query within a write lock based upon the cacheKey provided..
		/// </summary>
		/// <returns>Returns false if a timeout is reached.</returns>
		public bool Write(TKey key, Action closure, int? millisecondsTimeout = null, bool throwsOnTimeout = false)
		{
			return Execute(key, LockType.Write, closure, millisecondsTimeout, throwsOnTimeout);
		}

		/// <summary>
		/// Executes the query within a write lock based upon the cacheKey provided..
		/// </summary>
		/// <returns>Returns false if a timeout is reached.</returns>
		public bool Write<T>(
			TKey key, out T result, Func<T> closure,
			int? millisecondsTimeout = null, bool throwsOnTimeout = false)
		{
			return Execute(key, LockType.Write, out result, closure, millisecondsTimeout, throwsOnTimeout);
		}

		/// <summary>
		/// Executes the query within a read lock based upon the cacheKey provided..
		/// Throws a TimeoutException if a lock could not be acquired within the specified millisecondsTimeout.
		/// </summary>
		/// <returns>Returns the addValue from the valueFactory.</returns>
		public T ReadValue<T>(TKey key, Func<T> valueFactory, int? millisecondsTimeout = null)
		{
            Read(key, out T result, valueFactory, millisecondsTimeout, true);
            return result;
		}

		/// <summary>
		/// Executes the query within a write lock based upon the cacheKey provided..
		/// Throws a TimeoutException if a lock could not be acquired within the specified millisecondsTimeout.
		/// </summary>
		/// <returns>Returns the addValue from the valueFactory.</returns>
		public T WriteValue<T>(TKey key, Func<T> valueFactory, int? millisecondsTimeout = null)
		{
            Write(key, out T result, valueFactory, millisecondsTimeout, true);
            return result;
		}

		/// <summary>
		/// Method for synchronizing write access.  Starts by executing the condition without a lock.
		/// Note: Passing a bool to the condition when a lock is acquired helps if it is important to the cosuming logic to avoid recursive locking.
		/// </summary>
		/// <param name="condition">Takes a bool where false means no lock and true means a write lock.  Returns true if it should execute the query Action. ** NOT THREAD SAFE</param>
		/// <returns>Returns false if a timeout is reached.</returns>
		protected bool WriteConditional(TKey key, Func<bool, bool> condition, Action closure, int? millisecondsTimeout = null, bool throwsOnTimeout = false)
		{
			if (condition == null)
				throw new ArgumentNullException("condition");

			var lockHeld = true;
			if (condition(false)) // Thread safety is broken here.  But this method can be used as internal utility.
			{
				lockHeld = Write(key, () =>
				{
					if (condition(true))
						closure();
				},
				millisecondsTimeout,
				throwsOnTimeout);
			}

			return lockHeld;
		}

		/// <summary>
		/// Method for synchronizing write access.  Starts by executing the condition with a read lock.  Then if necessary after releasing the read lock, acquires a write lock.
		/// Note: Passing a LockType to the condition when a lock is acquired helps if it is important to the cosuming logic to avoid recursive locking.
		/// </summary>
		/// <param name="condition">Takes a bool where false means a read lock and true means a write lock.  Returns true if it should execute the query Action.</param>
		/// <returns>Returns false if a timeout is reached.</returns>
		public bool ReadWriteConditional(
			TKey key, Func<LockType, bool> condition, Action closure,
			int? millisecondsTimeout = null, bool throwsOnTimeout = false)
		{
			if (condition == null)
				throw new ArgumentNullException("condition");

			bool c = false;
			var lockHeld = Read(key, () => c = condition(LockType.Read), millisecondsTimeout, throwsOnTimeout);
			if (lockHeld && c)
			{
				lockHeld = Write(key, () =>
				{
					if (condition(LockType.Write))
						closure();
				},
				millisecondsTimeout, throwsOnTimeout);
			}

			return lockHeld;
		}

		/// <summary>
		/// Method for synchronizing write access.  Starts by executing the condition with a read lock.  Then if necessary after releasing the read lock, acquires a write lock.
		/// Note: Passing a LockType to the condition when a lock is acquired helps if it is important to the cosuming logic to avoid recursive locking.
		/// </summary>
		/// <param name="condition">Takes a bool where false means a read lock and true means a write lock.  Returns true if it should execute the query Action.</param>
		/// <returns>Returns false if a timeout is reached.</returns>
		public bool ReadWriteConditional<T>(
			ref T result,
			TKey key, Func<LockType, bool> condition, Func<T> closure,
			int? millisecondsTimeout = null, bool throwsOnTimeout = false)
		{
			if (condition == null)
				throw new ArgumentNullException("condition");

			var r = result;
			bool c = false, written = false;
			var lockHeld = Read(key, () => c = condition(LockType.Read), millisecondsTimeout, throwsOnTimeout);
			if (lockHeld && c)
			{
				lockHeld = Write(key, out written, () =>
				{
					var w = condition(LockType.Write);
					if (w) r = closure();
					return w;
				},
				millisecondsTimeout, throwsOnTimeout);
			}

			if (written)
				result = r;

			return lockHeld;
		}

		/// <summary>
		/// Method for synchronizing write access.  Starts by executing the condition with an upgradeable read lock before acquiring a write lock.
		/// Note: Passing a boolean to the condition when a lock is acquired helps if it is important to the cosuming logic to avoid recursive locking.
		/// </summary>
		/// <param name="condition">Takes a bool where false means a read lock and true means a write lock.  Returns true if it should execute the query Action.</param>
		/// <returns>Returns false if a timeout is reached.</returns>
		public bool ReadUpgradeableWriteConditional(
			TKey key, Func<bool> condition, Action closure,
			int? millisecondsTimeout = null, bool throwsOnTimeout = false)
		{
			if (condition == null)
				throw new ArgumentNullException("condition");

			// Initialize true so that if only only reading it still returns true.
			bool writeLocked = true;
			// Since read upgradable ensures that no changes should be made to the condition, then it is acceptable to not recheck the condition after lock...
			bool readLocked = ReadUpgradeable(key, (rwlock) =>
			{
				if (condition())
					// Synchronize lock acquisistion.
					writeLocked = rwlock.Write(closure, millisecondsTimeout, throwsOnTimeout);
			});

			return readLocked && writeLocked;
		}

		/// <summary>
		/// Method for synchronizing write access.  Starts by executing the condition with an upgradeable read lock before acquiring a write lock.
		/// Note: Passing a boolean to the condition when a lock is acquired helps if it is important to the cosuming logic to avoid recursive locking.
		/// </summary>
		/// <param name="condition">Takes a bool where false means a read lock and true means a write lock.  Returns true if it should execute the query Action.</param>
		/// <returns>Returns false if a timeout is reached.</returns>
		public bool ReadUpgradeableWriteConditional<T>(
			ref T result,
			TKey key, Func<bool> condition, Func<T> closure,
			int? millisecondsTimeout = null, bool throwsOnTimeout = false)
		{
			if (condition == null)
				throw new ArgumentNullException("condition");

			var r = result;
			bool writeLocked = true; // Initialize true so that if only only reading it still returns true.
			bool written = false;
			// Since read upgradable ensures that no changes should be made to the condition, then it is acceptable to not recheck the condition after lock...
			bool readLocked = ReadUpgradeable(key, (rwlock) =>
			{
				if (condition())
				{
					// Synchronize lock acquisistion.
					writeLocked = rwlock.Write(out r, closure, millisecondsTimeout, throwsOnTimeout);
					written = true;
				}
			});
			if (written)
				result = r;


			return readLocked && writeLocked;
		}

		/// <summary>
		/// ReaderWriterLockSlim extension for synchronizing write access.  Starts by executing the condition with a read lock.
		/// Then if necessary executes the condition with an upgradeable read lock before acquiring a write lock.
		/// </summary>
		/// <param name="condition">Takes a bool where false means a read lock and true means a write lock.  Returns true if it should execute the query Action.</param>
		/// <returns>Returns false if a timeout is reached.</returns>
		public bool ReadWriteConditionalOptimized(
			TKey key, Func<LockType, bool> condition, Action closure,
			int? millisecondsTimeout = null, bool throwsOnTimeout = false)
		{
			if (condition == null)
				throw new ArgumentNullException("condition");

			bool c = false;
			var lockHeld = Read(key, () => c = condition(LockType.Read), millisecondsTimeout, throwsOnTimeout);
			return lockHeld && (!c || ReadUpgradeableWriteConditional(key, () => condition(LockType.ReadUpgradeable), closure, millisecondsTimeout, throwsOnTimeout));
		}

		/// <summary>
		/// ReaderWriterLockSlim extension for synchronizing write access.  Starts by executing the condition with a read lock.
		/// Then if necessary executes the condition with an upgradeable read lock before acquiring a write lock.
		/// </summary>
		/// <param name="condition">Takes a bool where false means a read lock and true means a write lock.  Returns true if it should execute the query Action.</param>
		/// <returns>Returns false if a timeout is reached.</returns>
		public bool ReadWriteConditionalOptimized<T>(
			TKey key, ref T result, Func<LockType, bool> condition, Func<T> closure,
			int? millisecondsTimeout = null, bool throwsOnTimeout = false)
		{
			if (condition == null)
				throw new ArgumentNullException("condition");

			bool c = false;
			var lockHeld = Read(key, () => c = condition(LockType.Read), millisecondsTimeout, throwsOnTimeout);
			return lockHeld && (!c || ReadUpgradeableWriteConditional(ref result, key, () => condition(LockType.ReadUpgradeable), closure, millisecondsTimeout, throwsOnTimeout));
		}
		#endregion



		#region Cleanpup & Dispose
		private void CleanupInternal()
		{
			// Search for dormant locks.
			foreach (var key in Locks.Keys.ToArray())
			{
				if (key == null)
					throw new NullReferenceException("condition");

                if (Locks.TryGetValue(key, out ReaderWriterLockTracker tempLock) && tempLock != null) // This should be accurate and synchronized since we keep these locks private.
                {
                    if (tempLock.CanDispose && Locks.TryRemove(key, out tempLock) && tempLock != null)
                    {
                        if (Debugger.IsAttached && tempLock.IsDisposed && !this.IsDisposed)
                        {
                            // Possilby caused by an exception?
                            Debug.Fail("A tracker was disposed while in the lock registry.");
                        }

                        //lock (tempLock)
                        //{
                        if (!tempLock.IsDisposed)
                        {
                            if (tempLock.CanDispose)
                            {
                                if (this.IsDisposed)
                                {
                                    // Don't add back to the pool, just get rid of..
                                    tempLock.Dispose();
                                }
                                else
                                {
                                    try
                                    {
                                        LockPool.Add(tempLock);
                                    }
                                    catch (ObjectDisposedException)
                                    {
                                    }
                                }
                            }
                            else
                                // Just in case something happens on remove...
                                Locks.TryAdd(key, tempLock);
                        }
                        //}
                    }
                }
            }
		}

		protected void UpdateCleanupDelay()
		{
			// This presents a maximum delay of 10 seconds and if the number of lock counts get's over 100 it will decrease the time before cleanup.
			if (!IsDisposed)
				CleanupDelay = Math.Max(Math.Min(1000000 / (Locks.Count + 1), 10000), 1000); // Don't allow for less than.
		}

		private void ConcurrentBagTrim<T>(ConcurrentBag<T> target, int maxCount = 0, bool dispose = false, bool allowExceptions = true)
		{
			if (target == null)
				throw new ArgumentNullException("target");
            //var d = new List<IDisposable>();
            try
            {
                while (!target.IsEmpty && target.Count > maxCount && target.TryTake(out T context))
                {
                    if (dispose)
                    {
                        if (context is IDisposable i)
                        {
                            //d.Add(i);
                            try
                            {
                                i.Dispose();
                            }
                            catch
                            {
                                if (allowExceptions)
                                    throw;
                            }
                        }
                    }
                }

                if (Debugger.IsAttached && target.Where(t => t is DisposableBase).Cast<DisposableBase>().Any(t => t.IsDisposed))
                    Debug.Fail("Disposed object retained in bag.");
            }
            catch
            {
                if (allowExceptions)
                    throw;
            }
            // Complete the disposing in another thread since the profiler may be confused about it's reference at snapshot time.
            //if (dispose && d.Any())
            //Task.Factory.StartNew(() => d.DisposeAll());
        }

		protected override void OnCleanup()
		{

			// Prevents new locks from being acquired while a cleanup is active.
			bool lockHeld = CleanupManager.WriteConditional(write => !this.IsDisposed, () =>
			{
				UpdateCleanupDelay();

				CleanupInternal();

				var count = Locks.Count;
				var maxCount = Math.Min(count, 100);

				ConcurrentBagTrim(ContextPool, maxCount * 2);
				ConcurrentBagTrim(LockPool, maxCount, true);//ConcurrentQueueTrimAndDispose(LockPool, maxCount);

				if (Debugger.IsAttached && LockPool.Any(l => l.IsDisposed))
					Debug.Fail("LockPool is retaining a disposed tracker.");

				if (count == 0)
					ClearCleanup();

			}, 10000); // Use a timeout to ensure a busy collection isn't over locked.

			if (!IsDisposed)
			{
				if (!lockHeld) // Defer it since it wasn't done.
				{
					// Just to be sure...
					Debug.WriteLine("ReadWriteHelper cleanup deferred.");
					DeferCleanup();
				}
			}
		}

		protected override void OnDispose(bool calledExplicitly)
		{
			base.OnDispose(calledExplicitly);

			bool lockHeld = CleanupManager.Write(() =>
			{

				CleanupInternal();
				Locks.Clear(); // Some locks may be removed, but releasing will still occur.
				ConcurrentBagTrim(LockPool, 0, true, calledExplicitly);

			}, calledExplicitly ? 1000 : 0); // We don't want to block for any reason for too long.
											 // Dispose shouldn't be called without this being able to be cleaned.

			CleanupManager.Dispose(); // No more locks can be added after this...

			if (!lockHeld)
			{
				// Just to be sure...
				Debug.WriteLineIf(calledExplicitly, "ReadWriteHelper unable to synchronize during dispose.");
				CleanupInternal(); // Migrates to LockPool for cleanup or disposal...
			}

			Locks.Clear();

			ConcurrentBagTrim(ContextPool, 0, false, calledExplicitly);
			if (!lockHeld)
				ConcurrentBagTrim(LockPool, 0, true, calledExplicitly);

			if (calledExplicitly && Debugger.IsAttached)
			{
				if (Locks.Any())
					Debug.Fail("A lock was added after dispose.");
				if (LockPool.Any())
					Debug.Fail("Remaining trackers in lock pool.");
				if (ContextPool.Any())
					Debug.Fail("Remaining objects in context pool.");
			}
		}
		#endregion

	}
}
