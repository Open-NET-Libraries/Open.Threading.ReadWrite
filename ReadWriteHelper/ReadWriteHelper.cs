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
	#region Construction
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
	#endregion

	#region Lock Acquisition
	private bool TryGetLock(
		TKey key,
		LockType type,
		object context,
#if NETSTANDARD2_1_OR_GREATER
		[System.Diagnostics.CodeAnalysis.MaybeNullWhen(false)]
#endif
		out ReaderWriterLockTracker tracker,
		out LockDisposeHandler handler,
		int timeout = Timeout.Infinite,
		bool throwIfTimeout = false)
	{
		if (key is null)
			throw new ArgumentNullException(nameof(key));
		if (context is null)
			throw new ArgumentNullException(nameof(context));
		Contract.EndContractBlock();

		if (WasDisposed)
		{
			tracker = default!;
			handler = default;
			return false;
		}

		// Need to be able to enter a lock before releasing access in order to prevent removal..
		var (r, rwl) = CleanupManager.Read(
			() =>
			{
				// It is possible that a read could be acquired while disposing just before the dispose.
				if (WasDisposed)
					return (null, null);

				// Get a tracker..
				ReaderWriterLockTracker tracker;
				{
					// Compare the tracker retrieved with the one created..
					ReaderWriterLockTracker? created = null;
					do
					{
						tracker = Locks.GetOrAdd(key, _ => created = LockPool.Take());
					}
					// Safeguard against rare case of when a disposed tracker is retained via an exception (possibly?). :(
					while (!WasDisposed && tracker.WasDisposed);

					// If the one created is not the one retrieved, go ahead and add it to the pool so it doesn't go to waste.
					if (created is not null && created != tracker)
					{
						if (WasDisposed)
							created.Dispose();
						else
							LockPool.Give(created);
					}

					// This should never get out of sync, but just in case..
					var rlock = tracker.Lock;
					if (rlock is null || tracker.WasDisposed)
					{
						Debug.Fail("A lock tracker was retained after it was disposed.");
						return (null, null);
					}
#if DEBUG
					if (Debugger.IsAttached && rlock.RecursionPolicy == LockRecursionPolicy.NoRecursion)
					{
						if (rlock.IsWriteLockHeld && type == LockType.Read)
							Debugger.Break(); // 
					}
#endif
				}

				// Quick check to avoid further processes..
				return WasDisposed ? (null, null)
					: (tracker.TryGetLock(context, type, timeout, throwIfTimeout), tracker);
			});

		// In the rare case that a dispose could be initiated during this Read:
		// We need to not propagate locking..
		if(r is null)
		{
			tracker = default!;
			handler = default;
			return false;
		}

		if(WasDisposed)
		{
			r.Dispose();
			tracker = default!;
			handler = default;
			return false;
		}

		Debug.Assert(tracker?.Lock is not null);
		tracker = rwl!;

		handler = new LockDisposeHandler(r, AfterRelease);
		return true;
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
		// Now that we've realeased the lock, signal for cleanup later..
		SetCleanup(CleanupMode.ImmediateDeferredIfPastDue);
	}
	#endregion

	#region TryExecute
	// Funnel all delegates through here to ensure proper procedure for getting and releasing locks.
	private bool TryExecute(
		TKey key,
		LockType type,
		int timeout,
		bool throwIfTimeout,
		Action<ReaderWriterLockSlim> closure)
	{
		Debug.Assert(key is not null);
		Debug.Assert(closure is not null);

		using var context = ContextPool.Rent();
		if (!TryGetLock(key, type, context, out var tracker, out var handler, timeout, throwIfTimeout))
			return false;

		try
		{
			using(handler) closure!(tracker.Lock!);
		}
		finally
		{
			tracker.Clear(context);
		}

		return true;
	}

	private bool TryExecute<T>(
		TKey key,
		LockType type,
		int timeout,
		bool throwIfTimeout,
		out T result,
		Func<ReaderWriterLockSlim, T> closure)
	{
		Debug.Assert(key is not null);
		Debug.Assert(closure is not null);

		using var context = ContextPool.Rent();
		if (!TryGetLock(key, type, context, out var tracker, out var handler, timeout, throwIfTimeout))
		{
			result = default!;
			return false;
		}

		try
		{
			using (handler) result = closure!(tracker.Lock!);
		}
		finally
		{
			tracker.Clear(context);
		}

		return true;
	}

	// Funnel all delegates through here to ensure proper procedure for getting and releasing locks.
	private bool TryExecute(
		TKey key,
		LockType type,
		int timeout,
		bool throwIfTimeout,
		Action closure)
	{
		Debug.Assert(key is not null);
		Debug.Assert(closure is not null);

		using var context = ContextPool.Rent();
		if (!TryGetLock(key, type, context, out var tracker, out var handler, timeout, throwIfTimeout))
			return false;

		try
		{
			using (handler) closure!();
		}
		finally
		{
			tracker.Clear(context);
		}

		return true;
	}

	// Funnel all delegates through here to ensure proper procedure for getting and releasing locks.
	private bool TryExecute<T>(
		TKey key,
		LockType type,
		int timeout,
		bool throwIfTimeout,
		out T result,
		Func<T> closure)
	{
		Debug.Assert(key is not null);
		Debug.Assert(closure is not null);

		using var context = ContextPool.Rent();
		if (!TryGetLock(key, type, context, out var tracker, out var handler, timeout, throwIfTimeout))
		{
			result = default!;
			return false;
		}

		try
		{
			using (handler) result = closure!();
		}
		finally
		{
			tracker.Clear(context);
		}

		return true;
	}
	#endregion

	#region Read/Write Public Interface
	/// <returns><b>true</b> if the action was invoked; otherwise <b>false</b> if a timeout is reached.</returns>
	/// <inheritdoc cref="Read(TKey, Action)"/>
	public bool TryRead(
		TKey key, LockTimeout timeout, Action closure, bool throwIfTimeout = false)
		=> TryExecute(key, LockType.Read, timeout, throwIfTimeout, closure);

	/// <returns><b>true</b> if the action was invoked; otherwise <b>false</b> if a timeout is reached.</returns>
	/// <inheritdoc cref="Read{T}(TKey, Func{T})"/>
	public bool TryRead<T>(
		TKey key, LockTimeout timeout, out T result, Func<T> valueFactory, bool throwIfTimeout = false)
		=> TryExecute(key, LockType.Read, timeout, throwIfTimeout, out result, valueFactory);

	/// <inheritdoc cref="TryReadUpgradeable{T}(TKey, LockTimeout, out T, Func{ReaderWriterLockSlim, T}, bool)"/>
	public bool TryReadUpgradeable(
		TKey key, LockTimeout timeout, Action<ReaderWriterLockSlim> closure, bool throwIfTimeout = false)
		=> TryExecute(key, LockType.UpgradableRead, timeout, throwIfTimeout, closure);

	/// <summary>
	/// Executes the query within an upgradeable read lock based upon the <paramref name="key"/> provided.
	/// </summary>
	/// <inheritdoc cref="TryRead(TKey, LockTimeout, Action, bool)"/>
	public bool TryReadUpgradeable<T>(
		TKey key, LockTimeout timeout, out T result, Func<ReaderWriterLockSlim, T> closure, bool throwIfTimeout = false)
		=> TryExecute(key, LockType.UpgradableRead, timeout, throwIfTimeout, out result, closure);

	/// <returns><b>true</b> if the action was invoked; otherwise <b>false</b> if a timeout is reached.</returns>
	/// <inheritdoc cref="Write{T}(TKey, Func{T})"/>
	public bool TryWrite(TKey key, LockTimeout timeout, Action closure, bool throwIfTimeout = false)
		=> TryExecute(key, LockType.Write, timeout, throwIfTimeout, closure);

	/// <inheritdoc cref="TryWrite(TKey, LockTimeout, Action, bool)"/>
	public bool TryWrite<T>(
		TKey key, LockTimeout timeout, out T result, Func<T> closure, bool throwIfTimeout = false)
		=> TryExecute(key, LockType.Write, timeout, throwIfTimeout, out result, closure);

	/// <summary>
	/// Invokes the <paramref name="action"/> within a <b>read</b> lock based upon the <paramref name="key"/> provided.
	/// </summary>
	public void Read(TKey key, Action action)
	{
		var ok = TryExecute(key, LockType.Read, Timeout.Infinite, true, action);
		Debug.Assert(ok);
	}
	/// <summary>
	/// Invokes the <paramref name="valueFactory"/> within a <b>read</b> lock based upon the <paramref name="key"/> provided.
	/// </summary>
	/// <returns>The result of the <paramref name="valueFactory"/>.</returns>
	public T Read<T>(TKey key, Func<T> valueFactory)
	{
		var ok = TryExecute(key, LockType.Read, Timeout.Infinite, true, out var result, valueFactory);
		Debug.Assert(ok);
		return result;
	}

	/// <summary>
	/// Invokes the <paramref name="action"/> within a <b>write</b> lock based upon the <paramref name="key"/> provided.
	/// </summary>
	public void Write(TKey key, Action action)
	{
		var ok = TryExecute(key, LockType.Write, Timeout.Infinite, true, action);
		Debug.Assert(ok);
	}

	/// <returns>Returns the result from the <paramref name="valueFactory"/>.</returns>
	/// <inheritdoc cref="Write(TKey, Action)"/>
	public T Write<T>(TKey key, Func<T> valueFactory)
	{
		var ok = TryExecute(key, LockType.Write, Timeout.Infinite, true, out var result, valueFactory);
		Debug.Assert(ok);
		return result;
	}

	/// <summary>
	/// Method for synchronizing write access.  Starts by executing the condition without a lock.
	/// Note: Passing a bool to the condition when a lock is acquired helps if it is important to the cosuming logic to avoid recursive locking.
	/// </summary>
	/// <param name="key">The key to lock by.</param>
	/// <param name="condition">Takes a bool where false means no lock and true means a <b>write</b> lock.  Returns true if it should execute the query Action. ** NOT THREAD SAFE</param>
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
	/// Method for synchronizing write access.  Starts by executing the condition with a <b>read</b> lock.  Then if necessary after releasing the read lock, acquires a <b>write</b> lock.
	/// Note: Passing a LockType to the condition when a lock is acquired helps if it is important to the cosuming logic to avoid recursive locking.
	/// </summary>
	/// <param name="key">The key to lock by.</param>
	/// <param name="condition">Takes a bool where false means a <b>read</b> lock and true means a <b>write</b> lock.  Returns true if it should execute the query Action.</param>
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
		var lockHeld = TryRead(key, () => c = condition(LockType.Read), timeout, throwIfTimeout);
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
	/// Method for synchronizing write access.  Starts by executing the condition with a <b>read</b> lock.  Then if necessary after releasing the read lock, acquires a <b>write</b> lock.
	/// Note: Passing a LockType to the condition when a lock is acquired helps if it is important to the cosuming logic to avoid recursive locking.
	/// </summary>
	/// <param name="result">The result from the operation.</param>
	/// <param name="key">The key to lock by.</param>
	/// <param name="condition">Takes a bool where false means a <b>read</b> lock and true means a <b>write</b> lock.  Returns true if it should execute the query Action.</param>
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
		var lockHeld = TryRead(key, () => c = condition(LockType.Read), timeout, throwIfTimeout);
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
	/// Method for synchronizing write access.  Starts by executing the condition with an upgradeable read lock before acquiring a <b>write</b> lock.
	/// Note: Passing a boolean to the condition when a lock is acquired helps if it is important to the cosuming logic to avoid recursive locking.
	/// </summary>
	/// <param name="key">The key to lock by.</param>
	/// <param name="condition">Takes a bool where false means a <b>read</b> lock and true means a <b>write</b> lock.  Returns true if it should execute the query Action.</param>
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
		// Since read upgradable ensures that no changes should be made to the condition, then it is acceptable to not recheck the condition after lock..
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
	/// Method for synchronizing write access.  Starts by executing the condition with an upgradeable read lock before acquiring a <b>write</b> lock.
	/// Note: Passing a boolean to the condition when a lock is acquired helps if it is important to the cosuming logic to avoid recursive locking.
	/// </summary>
	/// <param name="result">The result from the operation.</param>
	/// <param name="key">The key to lock by.</param>
	/// <param name="condition">Takes a bool where false means a <b>read</b> lock and true means a <b>write</b> lock.  Returns true if it should execute the query Action.</param>
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
		// Since read upgradable ensures that no changes should be made to the condition, then it is acceptable to not recheck the condition after lock..
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
	/// ReaderWriterLockSlim extension for synchronizing write access.  Starts by executing the condition with a <b>read</b> lock.
	/// Then if necessary executes the condition with an upgradeable read lock before acquiring a <b>write</b> lock.
	/// </summary>
	/// <param name="key">The key to lock by.</param>
	/// <param name="condition">Takes a bool where false means a <b>read</b> lock and true means a <b>write</b> lock.  Returns true if it should execute the query Action.</param>
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
		var lockHeld = TryRead(key, () => c = condition(LockType.Read), timeout, throwIfTimeout);
		return lockHeld && (!c || ReadUpgradeableWriteConditional(key, () => condition(LockType.UpgradableRead), closure, timeout, throwIfTimeout));
	}

	/// <summary>
	/// ReaderWriterLockSlim extension for synchronizing write access.  Starts by executing the condition with a <b>read</b> lock.
	/// Then if necessary executes the condition with an upgradeable read lock before acquiring a <b>write</b> lock.
	/// </summary>
	/// <param name="key">The key to lock by.</param>
	/// <param name="result">The result from the operation.</param>
	/// <param name="condition">Takes a bool where false means a <b>read</b> lock and true means a <b>write</b> lock.  Returns true if it should execute the query Action.</param>
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
		var lockHeld = TryRead(key, () => c = condition(LockType.Read), timeout, throwIfTimeout);
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
					// Don't add back to the pool, just get rid of.
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
				// Just in case something happens on remove..
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
		// Just to be sure..
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

		CleanupManager.Dispose(); // No more locks can be added after this..

		if (!lockHeld) CleanupInternal(); // Migrates to LockPool for cleanup or disposal..
		Locks.Clear();
		ContextPool.Dispose();
		if (!lockHeld) LockPool.Dispose();
	}
#endregion

}
