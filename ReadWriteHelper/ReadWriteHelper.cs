using Open.Disposable;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Contracts;

namespace Open.Threading;

/// <summary>
/// Utility class for synchronizing read write access to different resources in the same domain/scope.
/// This essentially has its own garbage collector to prevent building up memory/references to unused locks.
/// </summary>
public class ReadWriteHelper<TKey> : DeferredCleanupBase
{
	readonly IObjectPool<object> ContextPool;
	readonly ConcurrentQueueObjectPool<ReaderWriterLockTracker> LockPool;

	readonly ConcurrentDictionary<TKey, ReaderWriterLockTracker> Locks
		= new();

	readonly ReaderWriterLockSlim CleanupManager
		= new(LockRecursionPolicy.SupportsRecursion);

	/// <summary>
	/// Constructs a <see cref="ReadWriteHelper{TKey}"/>.
	/// </summary>
	public ReadWriteHelper(bool supportRecursion = false)
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
#if DEBUG
		if (Debugger.IsAttached)
			created.BeforeDispose += Debug_TrackerDisposedWhileInUse;
#endif
		return created;
	}

	/// <summary>
	/// The <see cref="LockRecursionPolicy"/> being used.
	/// </summary>
	public LockRecursionPolicy RecursionPolicy { get; }

	private ReaderWriterLockTracker? TryGetLock(
		TKey key,
		LockType type,
		object context,
		int timeout = Timeout.Infinite,
		bool throwIfTimeout = false)
	{
		if (key is null)
			throw new ArgumentNullException(nameof(key));
		if (context is null)
			throw new ArgumentNullException(nameof(context));
		Contract.EndContractBlock();

		if (WasDisposed)
			return null;

		// Need to be able to enter a lock before releasing access in order to prevent removal...
		var r = CleanupManager.Read(
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
						result = Locks.GetOrAdd(key, _ => created = LockPool.Take());
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
				var reserved = result.Reserve(context);
				if (reserved is null) return null; // could not acquire a lock.
				try
				{
					// result.Lock will only be null if the tracker has been disposed.
					lockHeld = ReadWriteHelper<TKey>.TryAcquireLock(reserved, type, timeout, throwIfTimeout);
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

		// In the rare case that a dispose could be initiated during this Read:
		// We need to not propagate locking...
		if (r is null || !WasDisposed) return r;

		ReleaseLock(r.Lock!, type);
		r.Clear(context);
		return null;
	}

#if DEBUG
	void Debug_TrackerDisposedWhileInUse(object sender, EventArgs e)
	{
		var tracker = (ReaderWriterLockTracker)sender;
		if (Locks.Select(kvp => kvp.Value).Contains(tracker))
			Debug.Fail("Attempting to dispose a tracker that is in use.");
		//if (LockPool.Contains(tracker))
		//	Debug.Fail("Attempting to dispose a tracker that is still availalbe in the pool.");
	}
#endif

	private void AfterRelease()
	{
		if (WasDisposed) return;
		UpdateCleanupDelay();

		//SetCleanup(CleanupMode.ImmediateSynchronous); 
		// Now that we've realeased the lock, signal for cleanup later...
		SetCleanup(CleanupMode.ImmediateDeferredIfPastDue);
	}

	// Funnel all delegates through here to ensure proper procedure for getting and releasing locks.
	private bool Execute(
		TKey key,
		LockType type,
		Action<ReaderWriterLockSlim> closure,
		int timeout,
		bool throwIfTimeout)
	{
		Debug.Assert(key is not null);
		Debug.Assert(closure is not null);

		using var context = ContextPool.Rent();

		var tracker = TryGetLock(key, type, context, timeout, throwIfTimeout);
		if (tracker is null)
			return false;
		var rwlock = tracker.Lock;
		if (rwlock is null)
			return false;

		try
		{
			closure!(rwlock);
		}
		finally
		{
			try
			{
				ReleaseLock(rwlock, type);
				tracker.Clear(context);
			}
			catch (Exception ex)
			{
				Debug.WriteLine(ex.ToString());
				// The above cannot fail or dire concequences...
				Debugger.Break();
#pragma warning disable CA2219 // Do not raise exceptions in finally clauses
				throw;
#pragma warning restore CA2219 // Do not raise exceptions in finally clauses
			}
		}
		return true;
	}

	private bool Execute<T>(
		TKey key,
		LockType type,
		out T result,
		Func<ReaderWriterLockSlim, T> closure,
		int timeout,
		bool throwIfTimeout)
	{
		T r = default!;

		var acquired = Execute(key, type,
			(rwlock) => r = closure(rwlock),
			timeout,
			throwIfTimeout);

		result = r!;

		return acquired;
	}

	// Funnel all delegates through here to ensure proper procedure for getting and releasing locks.
	private bool Execute(TKey key, LockType type, Action closure, int timeout = Timeout.Infinite, bool throwIfTimeout = false)
	{
		if (closure is null)
			throw new ArgumentNullException(nameof(closure));
		Contract.EndContractBlock();

		return Execute(key, type, (_) => closure(), timeout, throwIfTimeout);
	}

	// Funnel all delegates through here to ensure proper procedure for getting and releasing locks.
	private bool Execute<T>(TKey key, LockType type, out T result, Func<T> closure, int timeout = Timeout.Infinite, bool throwIfTimeout = false)
	{
		if (closure is null)
			throw new ArgumentNullException(nameof(closure));
		Contract.EndContractBlock();

		return Execute(key, type, out result, (_) => closure(), timeout, throwIfTimeout);
	}

#region Read/Write Public Interface
	/// <summary>
	/// Executes the query within a read lock based upon the <paramref name="key"/> provided..
	/// </summary>
	/// <returns>Returns false if a timeout is reached.</returns>
	public bool Read(
		TKey key, Action closure,
		LockTimeout timeout = default, bool throwIfTimeout = false)
		=> Execute(key, LockType.Read, closure, timeout, throwIfTimeout);

	/// <summary>
	/// Executes the query within a read lock based upon the <paramref name="key"/> provided..
	/// </summary>
	/// <returns>Returns false if a timeout is reached.</returns>
	public bool Read<T>(
		TKey key, out T result, Func<T> valueFactory,
		LockTimeout timeout = default, bool throwIfTimeout = false)
		=> Execute(key, LockType.Read, out result, valueFactory, timeout, throwIfTimeout);

	/// <summary>
	/// Executes the query within an upgradeable read lock based upon the <paramref name="key"/> provided..
	/// </summary>
	/// <returns>Returns false if a timeout is reached.</returns>
	private bool ReadUpgradeable(
		TKey key, Action<ReaderWriterLockSlim> closure,
		LockTimeout timeout = default, bool throwIfTimeout = false)
		=> Execute(key, LockType.UpgradableRead, closure, timeout, throwIfTimeout);

	/// <summary>
	/// Executes the query within an upgradeable read lock based upon the <paramref name="key"/> provided..
	/// </summary>
	/// <returns>Returns false if a timeout is reached.</returns>
	public bool ReadUpgradeable<T>(
		TKey key, out T result, Func<T> closure,
		LockTimeout timeout = default, bool throwIfTimeout = false)
		=> Execute(key, LockType.UpgradableRead, out result, closure, timeout, throwIfTimeout);

	/// <summary>
	/// Executes the query within an upgradeable read lock based upon the <paramref name="key"/> provided..
	/// </summary>
	/// <returns>Returns false if a timeout is reached.</returns>
	public bool ReadUpgradeable(
		TKey key, Action closure,
		LockTimeout timeout = default, bool throwIfTimeout = false)
		=> Execute(key, LockType.UpgradableRead, closure, timeout, throwIfTimeout);

	/// <summary>
	/// Executes the query within a write lock based upon the <paramref name="key"/> provided..
	/// </summary>
	/// <returns>Returns false if a timeout is reached.</returns>
	public bool Write(TKey key, Action closure, LockTimeout timeout = default, bool throwIfTimeout = false)
		=> Execute(key, LockType.Write, closure, timeout, throwIfTimeout);

	/// <summary>
	/// Executes the query within a write lock based upon the <paramref name="key"/> provided..
	/// </summary>
	/// <returns>Returns false if a timeout is reached.</returns>
	public bool Write<T>(
		TKey key, out T result, Func<T> closure,
		LockTimeout timeout = default, bool throwIfTimeout = false)
		=> Execute(key, LockType.Write, out result, closure, timeout, throwIfTimeout);

	/// <summary>
	/// Executes the query within a read lock based upon the <paramref name="key"/> provided..
	/// Throws a TimeoutException if a lock could not be acquired within the specified timeout.
	/// </summary>
	/// <returns>Returns the addValue from the valueFactory.</returns>
	public T ReadValue<T>(TKey key, Func<T> valueFactory, LockTimeout timeout = default)
	{
		Read(key, out var result, valueFactory, timeout, true);
		return result;
	}

	/// <summary>
	/// Executes the query within a write lock based upon the <paramref name="key"/> provided..
	/// Throws a TimeoutException if a lock could not be acquired within the specified timeout.
	/// </summary>
	/// <returns>Returns the addValue from the valueFactory.</returns>
	public T WriteValue<T>(TKey key, Func<T> valueFactory, LockTimeout timeout = default)
	{
		Write(key, out var result, valueFactory, timeout, true);
		return result;
	}

	/// <summary>
	/// Method for synchronizing write access.  Starts by executing the condition without a lock.
	/// Note: Passing a bool to the condition when a lock is acquired helps if it is important to the cosuming logic to avoid recursive locking.
	/// </summary>
	/// <param name="key">The key to lock by.</param>
	/// <param name="condition">Takes a bool where false means no lock and true means a write lock.  Returns true if it should execute the query Action. ** NOT THREAD SAFE</param>
	/// <param name="closure">Action to execute once a lock is acquired.</param>
	/// <param name="timeout">Indicates if and for how long a timeout is used to acquire a lock.</param>
	/// <param name="throwIfTimeout">If this parameter is true, then if a timeout addValue is reached, an exception is thrown.</param>
	/// <returns>Returns false if a timeout is reached.</returns>
	protected bool WriteConditional(TKey key, Func<bool, bool> condition, Action closure, LockTimeout timeout = default, bool throwIfTimeout = false)
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
			timeout,
			throwIfTimeout);
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
	/// <param name="timeout">Indicates if and for how long a timeout is used to acquire a lock.</param>
	/// <param name="throwIfTimeout">If this parameter is true, then if a timeout addValue is reached, an exception is thrown.</param>
	/// <returns>Returns false if a timeout is reached.</returns>
	public bool ReadWriteConditional(
		TKey key, Func<LockType, bool> condition, Action closure,
		LockTimeout timeout = default, bool throwIfTimeout = false)
	{
		if (condition is null)
			throw new ArgumentNullException(nameof(condition));
		Contract.EndContractBlock();

		var c = false;
		var lockHeld = Read(key, () => c = condition(LockType.Read), timeout, throwIfTimeout);
		if (lockHeld && c)
		{
			lockHeld = Write(key, () =>
			{
				if (condition(LockType.Write))
					closure();
			},
			timeout, throwIfTimeout);
		}

		return lockHeld;
	}

	/// <summary>
	/// Method for synchronizing write access.  Starts by executing the condition with a read lock.  Then if necessary after releasing the read lock, acquires a write lock.
	/// Note: Passing a LockType to the condition when a lock is acquired helps if it is important to the cosuming logic to avoid recursive locking.
	/// </summary>
	/// <param name="result">The result from the operation.</param>
	/// <param name="key">The key to lock by.</param>
	/// <param name="condition">Takes a bool where false means a read lock and true means a write lock.  Returns true if it should execute the query Action.</param>
	/// <param name="closure">Action to execute once a lock is acquired.</param>
	/// <param name="timeout">Indicates if and for how long a timeout is used to acquire a lock.</param>
	/// <param name="throwIfTimeout">If this parameter is true, then if a timeout addValue is reached, an exception is thrown.</param>
	/// <returns>Returns false if a timeout is reached.</returns>
	public bool ReadWriteConditional<T>(
		ref T result,
		TKey key, Func<LockType, bool> condition, Func<T> closure,
		LockTimeout timeout = default, bool throwIfTimeout = false)
	{
		if (condition is null)
			throw new ArgumentNullException(nameof(condition));
		Contract.EndContractBlock();

		var r = result;
		bool c = false, written = false;
		var lockHeld = Read(key, () => c = condition(LockType.Read), timeout, throwIfTimeout);
		if (lockHeld && c)
		{
			lockHeld = Write(key, out written, () =>
			{
				var w = condition(LockType.Write);
				if (w) r = closure();
				return w;
			},
			timeout, throwIfTimeout);
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
	/// <param name="timeout">Indicates if and for how long a timeout is used to acquire a lock.</param>
	/// <param name="throwIfTimeout">If this parameter is true, then if a timeout addValue is reached, an exception is thrown.</param>
	/// <returns>Returns false if a timeout is reached.</returns>
	public bool ReadUpgradeableWriteConditional(
		TKey key, Func<bool> condition, Action<bool> closure,
		LockTimeout timeout = default,
		bool throwIfTimeout = false)
	{
		if (condition is null)
			throw new ArgumentNullException(nameof(condition));
		Contract.EndContractBlock();

		// Initialize true so that if only only reading it still returns true.
		var writeLocked = true;
		// Since read upgradable ensures that no changes should be made to the condition, then it is acceptable to not recheck the condition after lock...
		var readLocked = ReadUpgradeable(key, (rwlock) =>
		{
			if (!condition())
				return;

			// Synchronize lock acquisistion.
			writeLocked = rwlock.Write(closure, timeout, throwIfTimeout);
		}, timeout, throwIfTimeout);

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
	/// <param name="timeout">Indicates if and for how long a timeout is used to acquire a lock.</param>
	/// <param name="throwIfTimeout">If this parameter is true, then if a timeout addValue is reached, an exception is thrown.</param>
	/// <returns>Returns false if a timeout is reached.</returns>
	public bool ReadUpgradeableWriteConditional<T>(
		ref T result,
		TKey key, Func<bool> condition, Func<bool, T> closure,
		LockTimeout timeout = default, bool throwIfTimeout = false)
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
			writeLocked = rwlock.Write(timeout, out r, closure);
			written = true;
		}, timeout, throwIfTimeout);
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
	/// <param name="timeout">Indicates if and for how long a timeout is used to acquire a lock.</param>
	/// <param name="throwIfTimeout">If this parameter is true, then if a timeout addValue is reached, an exception is thrown.</param>
	/// <returns>Returns false if a timeout is reached.</returns>
	public bool ReadWriteConditionalOptimized(
		TKey key, Func<LockType, bool> condition, Action<bool> closure,
		LockTimeout timeout = default, bool throwIfTimeout = false)
	{
		if (condition is null)
			throw new ArgumentNullException(nameof(condition));
		Contract.EndContractBlock();

		var c = false;
		var lockHeld = Read(key, () => c = condition(LockType.Read), timeout, throwIfTimeout);
		return lockHeld && (!c || ReadUpgradeableWriteConditional(key, () => condition(LockType.UpgradableRead), closure, timeout, throwIfTimeout));
	}

	/// <summary>
	/// ReaderWriterLockSlim extension for synchronizing write access.  Starts by executing the condition with a read lock.
	/// Then if necessary executes the condition with an upgradeable read lock before acquiring a write lock.
	/// </summary>
	/// <param name="key">The key to lock by.</param>
	/// <param name="result">The result from the operation.</param>
	/// <param name="condition">Takes a bool where false means a read lock and true means a write lock.  Returns true if it should execute the query Action.</param>
	/// <param name="closure">Action to execute once a lock is acquired.</param>
	/// <param name="timeout">Indicates if and for how long a timeout is used to acquire a lock.</param>
	/// <param name="throwIfTimeout">If this parameter is true, then if a timeout addValue is reached, an exception is thrown.</param>
	/// <returns>Returns false if a timeout is reached.</returns>
	public bool ReadWriteConditionalOptimized<T>(
		TKey key, ref T result, Func<LockType, bool> condition, Func<bool, T> closure,
		LockTimeout timeout = default, bool throwIfTimeout = false)
	{
		if (condition is null)
			throw new ArgumentNullException(nameof(condition));
		Contract.EndContractBlock();

		var c = false;
		var lockHeld = Read(key, () => c = condition(LockType.Read), timeout, throwIfTimeout);
		return lockHeld && (!c || ReadUpgradeableWriteConditional(ref result, key, () => condition(LockType.UpgradableRead), closure, timeout, throwIfTimeout));
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
			{
				// Just in case something happens on remove...
				Locks.TryAdd(key, tempLock);
			}
			//}
		}
	}

	/// <inheritdoc />
	protected void UpdateCleanupDelay()
	{
		// This presents a maximum delay of 10 seconds and if the number of lock counts get's over 100 it will decrease the time before cleanup.
		if (!WasDisposed)
			CleanupDelay = Math.Max(Math.Min(1000000 / (Locks.Count + 1), 10000), 1000); // Don't allow for less than.
	}

	/// <inheritdoc />
	protected override void OnCleanup()
	{
		// Prevents new locks from being acquired while a cleanup is active.
		var lockHeld = CleanupManager.WriteConditional(10000, _ => !WasDisposed, () =>
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
		}); // Use a timeout to ensure a busy collection isn't over locked.

		if (WasDisposed) return;
		if (lockHeld) return;
		// Just to be sure...
		Debug.WriteLine("ReadWriteHelper cleanup deferred.");
		DeferCleanup();
	}

	/// <inheritdoc />
	protected override void OnDispose()
	{
		base.OnDispose();

		var lockPool = LockPool;
		var lockHeld = CleanupManager.TryWrite(1000, () =>
		{
			CleanupInternal();
			Locks.Clear(); // Some locks may be removed, but releasing will still occur.
			lockPool.Dispose();
		}); // We don't want to block for any reason for too long.
		// Dispose shouldn't be called without this being able to be cleaned.

		CleanupManager.Dispose(); // No more locks can be added after this...

		if (!lockHeld) CleanupInternal(); // Migrates to LockPool for cleanup or disposal...
		Locks.Clear();
		ContextPool.Dispose();
		if (!lockHeld) LockPool.Dispose();
	}
#endregion

}
