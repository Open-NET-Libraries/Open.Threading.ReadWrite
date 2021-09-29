using Open.Disposable;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Contracts;
using System.Linq;
using System.Threading;

namespace Open.Threading;

/// <summary>
/// Utility class for synchronizing read write access to different resources in the same domain/scope.
/// This essentially has it's own garbage collector to prevent building up memory/references to unused locks.
/// </summary>
public class ReadWriteHelper<TKey> : DeferredCleanupBase
{

	class ReaderWriterLockTracker : DisposableBase
	{
		readonly HashSet<object> _registry = new();

		public ReaderWriterLockSlim? Lock;

		private ReaderWriterLockTracker(ReaderWriterLockSlim rwlock) => Lock = rwlock;

		public ReaderWriterLockTracker(LockRecursionPolicy policy)
			: this(new ReaderWriterLockSlim(policy))
		{
		}

		public bool Reserve(object context)
		{
			if (WasDisposed)
				return false;

			lock (_registry)
			{
				return _registry.Add(context);
			}
		}

		public void Clear(object context)
		{
			lock (_registry)
			{
				_registry.Remove(context);
			}
		}

		public bool CanDispose
		{
			get
			{
				lock (_registry)
				{
					return _registry.Count == 0;
				}
			}
		}

		protected override void OnDispose()
		{
			lock (_registry)
			{
				//Contract.Assume(CanDispose);
				// ReSharper disable once RedundantAssignment
				var count = _registry.Count;
				Debug.WriteLineIf(count != 0, "Disposing a ReaderWriterLockTracker with " + count + " contexts still registered.");
				_registry.Clear();
			}

			var l = Lock;
			Lock = null;
			l?.Dispose();
		}
	}


	readonly IObjectPool<object> ContextPool;
	readonly ConcurrentQueueObjectPool<ReaderWriterLockTracker> LockPool;

	readonly ConcurrentDictionary<TKey, ReaderWriterLockTracker> Locks
		= new();

	readonly ReaderWriterLockSlim CleanupManager
		= new(LockRecursionPolicy.SupportsRecursion);

	public ReadWriteHelper() : this(false)
	{
	}

	public ReadWriteHelper(bool supportRecursion)
	{
		//Debug.WriteLine("Constructing: "+this.ToString());

		RecursionPolicy = supportRecursion
			? LockRecursionPolicy.SupportsRecursion
			: LockRecursionPolicy.NoRecursion;

		ContextPool = OptimisticArrayObjectPool.Create<object>(32);

#if DEBUG
		static void recycle(ReaderWriterLockTracker rwlt) => Debug.Assert(rwlt.Lock!.IsLockFree());
#else
			Action<ReaderWriterLockTracker>? recycle = null;
#endif
		// ReSharper disable once ExpressionIsAlwaysNull
		LockPool = new ConcurrentQueueObjectPool<ReaderWriterLockTracker>(Factory, recycle, d => d.Dispose(), 256);
	}

	ReaderWriterLockTracker Factory()
	{
		var created = new ReaderWriterLockTracker(RecursionPolicy);
		if (Debugger.IsAttached)
			created.BeforeDispose += Debug_TrackerDisposedWhileInUse;
		return created;
	}

	public LockRecursionPolicy RecursionPolicy
	{
		get;
	}

	private ReaderWriterLockTracker? GetLock(TKey key, LockType type, object context, int? millisecondsTimeout = null, bool throwsOnTimeout = false)
	{
		if (key is null)
			throw new ArgumentNullException(nameof(key));
		if (context is null)
			throw new ArgumentNullException(nameof(context));
		ReaderWriterLockSlimExensions.ValidateMillisecondsTimeout(millisecondsTimeout);
		Contract.EndContractBlock();

		if (WasDisposed)
			return null;

		// Need to be able to enter a lock before releasing access in order to prevent removal...
		var r = CleanupManager.ReadValue(
			() =>
			{
					// It is possible that a read could be acquired while disposing just before the dispose.
					if (WasDisposed)
					return null;

					// Get a tracker...
					ReaderWriterLockTracker result;
				{
						// Compare the tracker retrieved with the one created...
						ReaderWriterLockTracker? created = null;
					do
					{
						result = Locks.GetOrAdd(key, k => created = LockPool.Take());
					}
						// Safeguard against rare case of when a disposed tracker is retained via an exception (possibly?). :(
						while (!WasDisposed && result.WasDisposed);


						// If the one created is not the one retrieved, go ahead and add it to the pool so it doesn't go to waste.
						if (created is not null && created != result)
					{
						if (WasDisposed)
							created.Dispose();
						else
							LockPool.Give(created);
					}

						// This should never get out of sync, but just in case...
						var rlock = result.Lock;
					if (rlock is null || result.WasDisposed)
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
					if (WasDisposed)
					return null;

				var lockHeld = false;
				if (!result.Reserve(context)) return null;
				try
				{
						// result.Lock will only be null if the tracker has been disposed.
						lockHeld = ReadWriteHelper<TKey>.AcquireLock(result.Lock, type, millisecondsTimeout, throwsOnTimeout);
				}
				catch (LockRecursionException lrex)
				{
					Debug.WriteLine(lrex.ToString());
					Debugger.Break(); // Need to be able to track down source.
						throw;
				}
				finally
				{
					if (!lockHeld)
						result.Clear(context);
				}

				return lockHeld ? result : null;
			});

		// In the rare case that a dispose could be initiated during this ReadValue:
		// We need to not propagate locking...
		if (r is null || !WasDisposed) return r;
		ReleaseLock(r.Lock, type);
		r.Clear(context);

		return null;

	}

	void Debug_TrackerDisposedWhileInUse(object sender, EventArgs e)
	{
		var tracker = (ReaderWriterLockTracker)sender;
		if (Locks.Select(kvp => kvp.Value).Contains(tracker))
			Debug.Fail("Attempting to dispose a tracker that is in use.");
		//if (LockPool.Contains(tracker))
		//	Debug.Fail("Attempting to dispose a tracker that is still availalbe in the pool.");
	}

	private static bool AcquireLock(ReaderWriterLockSlim? target, LockType type, int? millisecondsTimeout = null, bool throwsOnTimeout = false)
	{
		if (target is null)
			throw new ArgumentNullException(nameof(target));
		ReaderWriterLockSlimExensions.ValidateMillisecondsTimeout(millisecondsTimeout);
		Contract.EndContractBlock();

		return type switch
		{
			LockType.Read => target.EnterReadLock(millisecondsTimeout, throwsOnTimeout),
			LockType.ReadUpgradeable => target.EnterUpgradeableReadLock(millisecondsTimeout, throwsOnTimeout),
			LockType.Write => target.EnterWriteLock(millisecondsTimeout, throwsOnTimeout),
			_ => false,
		};
	}

	private void ReleaseLock(ReaderWriterLockSlim? target, LockType type)
	{
		if (target is null) return;
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

		if (WasDisposed) return;
		UpdateCleanupDelay();

		//SetCleanup(CleanupMode.ImmediateSynchronous); 
		// Now that we've realeased the lock, signal for cleanup later...
		SetCleanup(CleanupMode.ImmediateDeferredIfPastDue);
	}


	// Funnel all delegates through here to ensure proper procedure for getting and releasing locks.
	private bool Execute(TKey key, LockType type, Action<ReaderWriterLockSlim> closure, int? millisecondsTimeout = null, bool throwsOnTimeout = false)
	{
		if (key is null)
			throw new ArgumentNullException(nameof(key));
		if (closure is null)
			throw new ArgumentNullException(nameof(closure));
		ReaderWriterLockSlimExensions.ValidateMillisecondsTimeout(millisecondsTimeout);
		Contract.EndContractBlock();

		return ContextPool.Rent(context =>
		{
			var rwlock = GetLock(key, type, context, millisecondsTimeout, throwsOnTimeout);
			if (rwlock?.Lock is null)
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
				}
				catch (Exception ex)
				{
					Debug.WriteLine(ex.ToString());
						// The above cannot fail or dire concequences...
						Debugger.Break();
					throw;
				}
			}
			return true;
		});
	}

	private bool Execute<T>(
		TKey key,
		LockType type,
#if NETSTANDARD2_1
		[NotNullWhen(true)]
#endif
		out T result,
		Func<ReaderWriterLockSlim, T> closure,
		int? millisecondsTimeout = null,
		bool throwsOnTimeout = false)
	{
		T r = default!;

		var acquired = Execute(key, type, (rwlock) =>
		{
			r = closure(rwlock);
		}, millisecondsTimeout, throwsOnTimeout);

		result = r!;

		return acquired;
	}

	// Funnel all delegates through here to ensure proper procedure for getting and releasing locks.
	private bool Execute(TKey key, LockType type, Action closure, int? millisecondsTimeout = null, bool throwsOnTimeout = false)
	{
		if (closure is null)
			throw new ArgumentNullException(nameof(closure));
		Contract.EndContractBlock();

		return Execute(key, type, (rwlock) => closure(), millisecondsTimeout, throwsOnTimeout);
	}

	// Funnel all delegates through here to ensure proper procedure for getting and releasing locks.
	private bool Execute<T>(TKey key, LockType type, out T result, Func<T> closure, int? millisecondsTimeout = null, bool throwsOnTimeout = false)
	{
		if (closure is null)
			throw new ArgumentNullException(nameof(closure));
		Contract.EndContractBlock();

		return Execute(key, type, out result, (rwlock) => closure(), millisecondsTimeout, throwsOnTimeout);
	}

	#region Read/Write Public Interface
	/// <summary>
	/// Executes the query within a read lock based upon the cacheKey provided..
	/// </summary>
	/// <returns>Returns false if a timeout is reached.</returns>
	public bool Read(
		TKey key, Action closure,
		int? millisecondsTimeout = null, bool throwsOnTimeout = false) => Execute(key, LockType.Read, closure, millisecondsTimeout, throwsOnTimeout);

	/// <summary>
	/// Executes the query within a read lock based upon the cacheKey provided..
	/// </summary>
	/// <returns>Returns false if a timeout is reached.</returns>
	public bool Read<T>(
		TKey key, out T result, Func<T> valueFactory,
		int? millisecondsTimeout = null, bool throwsOnTimeout = false) => Execute(key, LockType.Read, out result, valueFactory, millisecondsTimeout, throwsOnTimeout);

	/// <summary>
	/// Executes the query within an upgradeable read lock based upon the cacheKey provided..
	/// </summary>
	/// <returns>Returns false if a timeout is reached.</returns>
	private bool ReadUpgradeable(
		TKey key, Action<ReaderWriterLockSlim> closure,
		int? millisecondsTimeout = null, bool throwsOnTimeout = false) => Execute(key, LockType.ReadUpgradeable, closure, millisecondsTimeout, throwsOnTimeout);

	/// <summary>
	/// Executes the query within an upgradeable read lock based upon the cacheKey provided..
	/// </summary>
	/// <returns>Returns false if a timeout is reached.</returns>
	public bool ReadUpgradeable<T>(
		TKey key, out T result, Func<T> closure,
		int? millisecondsTimeout = null, bool throwsOnTimeout = false) => Execute(key, LockType.ReadUpgradeable, out result, closure, millisecondsTimeout, throwsOnTimeout);

	/// <summary>
	/// Executes the query within an upgradeable read lock based upon the cacheKey provided..
	/// </summary>
	/// <returns>Returns false if a timeout is reached.</returns>
	public bool ReadUpgradeable(
		TKey key, Action closure,
		int? millisecondsTimeout = null, bool throwsOnTimeout = false) => Execute(key, LockType.ReadUpgradeable, closure, millisecondsTimeout, throwsOnTimeout);

	/// <summary>
	/// Executes the query within a write lock based upon the cacheKey provided..
	/// </summary>
	/// <returns>Returns false if a timeout is reached.</returns>
	public bool Write(TKey key, Action closure, int? millisecondsTimeout = null, bool throwsOnTimeout = false) => Execute(key, LockType.Write, closure, millisecondsTimeout, throwsOnTimeout);

	/// <summary>
	/// Executes the query within a write lock based upon the cacheKey provided..
	/// </summary>
	/// <returns>Returns false if a timeout is reached.</returns>
	public bool Write<T>(
		TKey key, out T result, Func<T> closure,
		int? millisecondsTimeout = null, bool throwsOnTimeout = false) => Execute(key, LockType.Write, out result, closure, millisecondsTimeout, throwsOnTimeout);

	/// <summary>
	/// Executes the query within a read lock based upon the cacheKey provided..
	/// Throws a TimeoutException if a lock could not be acquired within the specified millisecondsTimeout.
	/// </summary>
	/// <returns>Returns the addValue from the valueFactory.</returns>
	public T ReadValue<T>(TKey key, Func<T> valueFactory, int? millisecondsTimeout = null)
	{
		Read(key, out var result, valueFactory, millisecondsTimeout, true);
		return result;
	}

	/// <summary>
	/// Executes the query within a write lock based upon the cacheKey provided..
	/// Throws a TimeoutException if a lock could not be acquired within the specified millisecondsTimeout.
	/// </summary>
	/// <returns>Returns the addValue from the valueFactory.</returns>
	public T WriteValue<T>(TKey key, Func<T> valueFactory, int? millisecondsTimeout = null)
	{
		Write(key, out var result, valueFactory, millisecondsTimeout, true);
		return result;
	}

	/// <summary>
	/// Method for synchronizing write access.  Starts by executing the condition without a lock.
	/// Note: Passing a bool to the condition when a lock is acquired helps if it is important to the cosuming logic to avoid recursive locking.
	/// </summary>
	/// <param name="key">The key to lock by.</param>
	/// <param name="condition">Takes a bool where false means no lock and true means a write lock.  Returns true if it should execute the query Action. ** NOT THREAD SAFE</param>
	/// <param name="closure">Action to execute once a lock is acquired.</param>
	/// <param name="millisecondsTimeout">Indicates if and for how long a timeout is used to acquire a lock.</param>
	/// <param name="throwsOnTimeout">If this parameter is true, then if a timeout addValue is reached, an exception is thrown.</param>
	/// <returns>Returns false if a timeout is reached.</returns>
	protected bool WriteConditional(TKey key, Func<bool, bool> condition, Action closure, int? millisecondsTimeout = null, bool throwsOnTimeout = false)
	{
		if (condition is null)
			throw new ArgumentNullException(nameof(condition));
		Contract.EndContractBlock();

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
	/// <param name="key">The key to lock by.</param>
	/// <param name="condition">Takes a bool where false means a read lock and true means a write lock.  Returns true if it should execute the query Action.</param>
	/// <param name="closure">Action to execute once a lock is acquired.</param>
	/// <param name="millisecondsTimeout">Indicates if and for how long a timeout is used to acquire a lock.</param>
	/// <param name="throwsOnTimeout">If this parameter is true, then if a timeout addValue is reached, an exception is thrown.</param>
	/// <returns>Returns false if a timeout is reached.</returns>
	public bool ReadWriteConditional(
		TKey key, Func<LockType, bool> condition, Action closure,
		int? millisecondsTimeout = null, bool throwsOnTimeout = false)
	{
		if (condition is null)
			throw new ArgumentNullException(nameof(condition));
		Contract.EndContractBlock();

		var c = false;
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
	/// <param name="key">The key to lock by.</param>
	/// <param name="result">The result from the operation.</param>
	/// <param name="condition">Takes a bool where false means a read lock and true means a write lock.  Returns true if it should execute the query Action.</param>
	/// <param name="closure">Action to execute once a lock is acquired.</param>
	/// <param name="millisecondsTimeout">Indicates if and for how long a timeout is used to acquire a lock.</param>
	/// <param name="throwsOnTimeout">If this parameter is true, then if a timeout addValue is reached, an exception is thrown.</param>
	/// <returns>Returns false if a timeout is reached.</returns>
	public bool ReadWriteConditional<T>(
		ref T result,
		TKey key, Func<LockType, bool> condition, Func<T> closure,
		int? millisecondsTimeout = null, bool throwsOnTimeout = false)
	{
		if (condition is null)
			throw new ArgumentNullException(nameof(condition));
		Contract.EndContractBlock();

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
	/// <param name="key">The key to lock by.</param>
	/// <param name="condition">Takes a bool where false means a read lock and true means a write lock.  Returns true if it should execute the query Action.</param>
	/// <param name="closure">Action to execute once a lock is acquired.</param>
	/// <param name="millisecondsTimeout">Indicates if and for how long a timeout is used to acquire a lock.</param>
	/// <param name="throwsOnTimeout">If this parameter is true, then if a timeout addValue is reached, an exception is thrown.</param>
	/// <returns>Returns false if a timeout is reached.</returns>
	public bool ReadUpgradeableWriteConditional(
		TKey key, Func<bool> condition, Action closure,
		int? millisecondsTimeout = null, bool throwsOnTimeout = false)
	{
		if (condition is null)
			throw new ArgumentNullException(nameof(condition));
		Contract.EndContractBlock();

		// Initialize true so that if only only reading it still returns true.
		var writeLocked = true;
		// Since read upgradable ensures that no changes should be made to the condition, then it is acceptable to not recheck the condition after lock...
		var readLocked = ReadUpgradeable(key, (rwlock) =>
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
	/// <param name="result">The result from the operation.</param>
	/// <param name="key">The key to lock by.</param>
	/// <param name="condition">Takes a bool where false means a read lock and true means a write lock.  Returns true if it should execute the query Action.</param>
	/// <param name="closure">Action to execute once a lock is acquired.</param>
	/// <param name="millisecondsTimeout">Indicates if and for how long a timeout is used to acquire a lock.</param>
	/// <param name="throwsOnTimeout">If this parameter is true, then if a timeout addValue is reached, an exception is thrown.</param>
	/// <returns>Returns false if a timeout is reached.</returns>
	public bool ReadUpgradeableWriteConditional<T>(
		ref T result,
		TKey key, Func<bool> condition, Func<T> closure,
		int? millisecondsTimeout = null, bool throwsOnTimeout = false)
	{
		if (condition is null)
			throw new ArgumentNullException(nameof(condition));
		Contract.EndContractBlock();

		var r = result;
		var writeLocked = true; // Initialize true so that if only only reading it still returns true.
		var written = false;
		// Since read upgradable ensures that no changes should be made to the condition, then it is acceptable to not recheck the condition after lock...
		var readLocked = ReadUpgradeable(key, (rwlock) =>
		{
			if (!condition()) return;
				// Synchronize lock acquisistion.
				writeLocked = rwlock.Write(out r, closure, millisecondsTimeout, throwsOnTimeout);
			written = true;
		});
		if (written) result = r;

		return readLocked && writeLocked;
	}

	/// <summary>
	/// ReaderWriterLockSlim extension for synchronizing write access.  Starts by executing the condition with a read lock.
	/// Then if necessary executes the condition with an upgradeable read lock before acquiring a write lock.
	/// </summary>
	/// <param name="key">The key to lock by.</param>
	/// <param name="condition">Takes a bool where false means a read lock and true means a write lock.  Returns true if it should execute the query Action.</param>
	/// <param name="closure">Action to execute once a lock is acquired.</param>
	/// <param name="millisecondsTimeout">Indicates if and for how long a timeout is used to acquire a lock.</param>
	/// <param name="throwsOnTimeout">If this parameter is true, then if a timeout addValue is reached, an exception is thrown.</param>
	/// <returns>Returns false if a timeout is reached.</returns>
	public bool ReadWriteConditionalOptimized(
		TKey key, Func<LockType, bool> condition, Action closure,
		int? millisecondsTimeout = null, bool throwsOnTimeout = false)
	{
		if (condition is null)
			throw new ArgumentNullException(nameof(condition));
		Contract.EndContractBlock();

		var c = false;
		var lockHeld = Read(key, () => c = condition(LockType.Read), millisecondsTimeout, throwsOnTimeout);
		return lockHeld && (!c || ReadUpgradeableWriteConditional(key, () => condition(LockType.ReadUpgradeable), closure, millisecondsTimeout, throwsOnTimeout));
	}

	/// <summary>
	/// ReaderWriterLockSlim extension for synchronizing write access.  Starts by executing the condition with a read lock.
	/// Then if necessary executes the condition with an upgradeable read lock before acquiring a write lock.
	/// </summary>
	/// <param name="key">The key to lock by.</param>
	/// <param name="result">The result from the operation.</param>
	/// <param name="condition">Takes a bool where false means a read lock and true means a write lock.  Returns true if it should execute the query Action.</param>
	/// <param name="closure">Action to execute once a lock is acquired.</param>
	/// <param name="millisecondsTimeout">Indicates if and for how long a timeout is used to acquire a lock.</param>
	/// <param name="throwsOnTimeout">If this parameter is true, then if a timeout addValue is reached, an exception is thrown.</param>
	/// <returns>Returns false if a timeout is reached.</returns>
	public bool ReadWriteConditionalOptimized<T>(
		TKey key, ref T result, Func<LockType, bool> condition, Func<T> closure,
		int? millisecondsTimeout = null, bool throwsOnTimeout = false)
	{
		if (condition is null)
			throw new ArgumentNullException(nameof(condition));
		Contract.EndContractBlock();

		var c = false;
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
			if (key is null)
				throw new NullReferenceException();

			if (!Locks.TryGetValue(key, out var tempLock) || tempLock is null) continue;
			if (!tempLock.CanDispose || !Locks.TryRemove(key, out tempLock) || tempLock is null) continue;

#if DEBUG
			if (tempLock.WasDisposed && !WasDisposed)
			{
				// Possilby caused by an exception?
				Debug.Fail("A tracker was disposed while in the lock registry.");
			}
#endif

			//lock (tempLock)
			//{
			if (tempLock.WasDisposed) continue;
			if (tempLock.CanDispose)
			{
				if (WasDisposed)
				{
					// Don't add back to the pool, just get rid of..
					tempLock.Dispose();
				}
				else
				{
					try
					{
						LockPool.Give(tempLock);
					}
					catch (ObjectDisposedException)
					{
						// Rare case where lock pool get's disposed inbetween iterations.
					}
				}
			}
			else
				// Just in case something happens on remove...
				Locks.TryAdd(key, tempLock);
			//}
		}
	}

	protected void UpdateCleanupDelay()
	{
		// This presents a maximum delay of 10 seconds and if the number of lock counts get's over 100 it will decrease the time before cleanup.
		if (!WasDisposed)
			CleanupDelay = Math.Max(Math.Min(1000000 / (Locks.Count + 1), 10000), 1000); // Don't allow for less than.
	}

	//		private void TrimPool<T>(ConcurrentQueue<T> target, int maxCount = 0, bool dispose = false, bool allowExceptions = true)
	//		{
	//			if (target is null)
	//				throw new ArgumentNullException(nameof(target));
	//			Contract.EndContractBlock();

	//			//var d = new List<IDisposable>();
	//			try
	//			{
	//				while (!target.IsEmpty && target.Count > maxCount && target.TryDequeue(out var context))
	//				{
	//					if (!dispose) continue;
	//					if (!(context is IDisposable i)) continue;
	//					//d.Add(i);
	//					try
	//					{
	//						i.Dispose();
	//					}
	//					catch
	//					{
	//						if (allowExceptions)
	//							throw;
	//					}
	//				}

	//#if DEBUG
	//				if (target.Where(t => t is DisposableBase).Cast<DisposableBase>().Any(t => t.WasDisposed))
	//					Debug.Fail("Disposed object retained in bag.");	
	//#endif

	//			}
	//			catch
	//			{
	//				if (allowExceptions)
	//					throw;
	//			}
	//			// Complete the disposing in another thread since the profiler may be confused about it's reference at snapshot time.
	//			//if (dispose && d.Any())
	//			//Task.Factory.StartNew(() => d.DisposeAll());
	//		}

	protected override void OnCleanup()
	{

		// Prevents new locks from being acquired while a cleanup is active.
		var lockHeld = CleanupManager.WriteConditional(write => !WasDisposed, () =>
		{
			UpdateCleanupDelay();

			CleanupInternal();

			var count = Locks.Count;
			var maxCount = Math.Min(count, 100);

				//ContextPool.TrimTo(maxCount * 2);
				LockPool.TrimTo(maxCount);

				//if (Debugger.IsAttached && LockPool.Any(l => l.WasDisposed))
				//	Debug.Fail("LockPool is retaining a disposed tracker.");

				if (count == 0)
				ClearCleanup();

		}, 10000); // Use a timeout to ensure a busy collection isn't over locked.

		if (WasDisposed) return;
		if (lockHeld) return;
		// Just to be sure...
		Debug.WriteLine("ReadWriteHelper cleanup deferred.");
		DeferCleanup();
	}

	protected override void OnDispose()
	{
		base.OnDispose();

		var lockPool = LockPool;
		var lockHeld = CleanupManager.Write(() =>
		{
			CleanupInternal();
			Locks.Clear(); // Some locks may be removed, but releasing will still occur.
				lockPool.Dispose();
		}, 1000); // We don't want to block for any reason for too long.
				  // Dispose shouldn't be called without this being able to be cleaned.

		CleanupManager.Dispose(); // No more locks can be added after this...

		if (!lockHeld) CleanupInternal(); // Migrates to LockPool for cleanup or disposal...
		Locks.Clear();
		ContextPool.Dispose();
		if (!lockHeld) LockPool.Dispose();

	}
	#endregion

}
