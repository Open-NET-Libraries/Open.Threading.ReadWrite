using Open.Disposable;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;

namespace Open.Threading;

/// <summary>
/// Utility class for synchronizing read write access to different resources in the same domain/scope.
/// This essentially has its own garbage collector to prevent building up memory/references to unused locks.
/// </summary>
public class ReadWriteHelper<TContext> : DeferredCleanupBase
{
	#region Construction
	readonly IObjectPool<object> ContextPool;
	readonly ConcurrentQueueObjectPool<ReaderWriterLockTracker> LockPool;

	readonly ConcurrentDictionary<TContext, ReaderWriterLockTracker> Locks
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
		TContext key,
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
		TContext key,
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
		TContext key,
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
		TContext key,
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
		TContext key,
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

	/// <inheritdoc />
	public readonly record struct ContextHandler : IReadWriteLockingHandler<ReaderWriterLockSlim>
	{
		/// <summary>The key by which the context of locking occurs.</summary>
		public TContext Context { get; }

		/// <inheritdoc />
		private readonly ReadWriteHelper<TContext> Helper;

		internal ContextHandler(ReadWriteHelper<TContext> helper, TContext context)
		{
			Helper = helper;
			Context = context ?? throw new ArgumentNullException(nameof(context));
		}

		/// <inheritdoc />
		[ExcludeFromCodeCoverage]
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public bool Try(LockType lockType, LockTimeout timeout, Action<ReaderWriterLockSlim> action, bool throwIfTimeout = false)
			=> Helper.TryExecute(Context, lockType, timeout, throwIfTimeout, action);

		/// <inheritdoc />
		[ExcludeFromCodeCoverage]
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public bool Try<T>(LockType lockType, LockTimeout timeout, out T result, Func<ReaderWriterLockSlim, T> action, bool throwIfTimeout = false)
			=> Helper.TryExecute(Context, lockType, timeout, throwIfTimeout, out result, action);

		/// <inheritdoc />
		[ExcludeFromCodeCoverage]
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public bool Try(LockType lockType, LockTimeout timeout, Action action, bool throwIfTimeout = false)
			=> Helper.TryExecute(Context, lockType, timeout, throwIfTimeout, action);

		/// <inheritdoc />
		[ExcludeFromCodeCoverage]
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public bool Try<T>(LockType lockType, LockTimeout timeout, out T result, Func<T> action, bool throwIfTimeout = false)
			=> Helper.TryExecute(Context, lockType, timeout, throwIfTimeout, out result, action);

		/// <inheritdoc />
		[ExcludeFromCodeCoverage]
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public bool TryRead(LockTimeout timeout, Action action, bool throwIfTimeout = false)
			=> Helper.TryExecute(Context, LockType.Read, timeout, throwIfTimeout, action);

		/// <inheritdoc />
		[ExcludeFromCodeCoverage]
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public bool TryRead<T>(LockTimeout timeout, out T result, Func<T> action, bool throwIfTimeout = false)
			=> Helper.TryExecute(Context, LockType.Read, timeout, throwIfTimeout, out result, action);

		/// <inheritdoc />
		[ExcludeFromCodeCoverage]
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public bool TryReadUpgradable(LockTimeout timeout, Action<ReaderWriterLockSlim> action, bool throwIfTimeout = false)
			=> Helper.TryExecute(Context, LockType.UpgradableRead, timeout, throwIfTimeout, action);

		/// <inheritdoc />
		[ExcludeFromCodeCoverage]
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public bool TryReadUpgradable<T>(LockTimeout timeout, out T result, Func<ReaderWriterLockSlim, T> action, bool throwIfTimeout = false)
			=> Helper.TryExecute(Context, LockType.UpgradableRead, timeout, throwIfTimeout, out result, action);

		/// <inheritdoc />
		[ExcludeFromCodeCoverage]
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public bool TryReadUpgradable(LockTimeout timeout, Action action, bool throwIfTimeout = false)
			=> Helper.TryExecute(Context, LockType.UpgradableRead, timeout, throwIfTimeout, action);

		/// <inheritdoc />
		[ExcludeFromCodeCoverage]
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public bool TryReadUpgradable<T>(LockTimeout timeout, out T result, Func<T> action, bool throwIfTimeout = false)
			=> Helper.TryExecute(Context, LockType.UpgradableRead, timeout, throwIfTimeout, out result, action);

		/// <inheritdoc />
		[ExcludeFromCodeCoverage]
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public bool TryWrite(LockTimeout timeout, Action action, bool throwIfTimeout = false)
			=> Helper.TryExecute(Context, LockType.Write, timeout, throwIfTimeout, action);

		/// <inheritdoc />
		[ExcludeFromCodeCoverage]
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public bool TryWrite<T>(LockTimeout timeout, out T result, Func<T> action, bool throwIfTimeout = false)
			=> Helper.TryExecute(Context, LockType.Write, timeout, throwIfTimeout, out result, action);
	}

	/// <summary>
	/// Exposes an interface for read-write operations by context.
	/// </summary>
	public ContextHandler Context(TContext context) => new(this, context);

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
