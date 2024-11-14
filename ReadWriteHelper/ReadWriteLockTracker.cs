using Open.Disposable;
using System.Diagnostics;

namespace Open.Threading;

class ReaderWriterLockTracker : DisposableBase
{
	readonly HashSet<object> _registry = [];
#if NET9_0_OR_GREATER
	readonly System.Threading.Lock _lock = new();
#else
	readonly object _lock = new();
#endif

	public ReaderWriterLockSlim? Lock;

	private ReaderWriterLockTracker(ReaderWriterLockSlim rwlock)
		=> Lock = rwlock;

	public ReaderWriterLockTracker(LockRecursionPolicy policy)
		: this(new ReaderWriterLockSlim(policy)) { }

	public ReaderWriterLockSlim? Reserve(object context)
	{
		Debug.Assert(context is not null);
		lock (_lock)
		{
			var reserved = Lock;
			if (reserved is null || WasDisposed) return null;
			_registry.Add(context!);
			return reserved;
		}
	}

	public ILock? TryGetLock(object context, LockType type, int timeout, bool throwIfTimeout)
	{
		Debug.Assert(context is not null);
		var lockHeld = false;
		var reserved = Reserve(context!);
		if (reserved is null) return null; // could not acquire a lock.
		try
		{
			var iLock = reserved.GetLock(type, timeout, throwIfTimeout);
			lockHeld = iLock.LockHeld;
			if (lockHeld) return iLock;
			iLock.Dispose();
			return null;
		}
		catch (LockRecursionException lrex)
		{
			Debug.WriteLine(lrex.ToString());
			Debugger.Break(); // Need to be able to track down source.
			throw;
		}
		finally
		{
			if (!lockHeld) Clear(context!);
		}
	}

	public void Clear(object context)
	{
		Debug.Assert(context is not null);
		lock (_lock)
		{
			_registry.Remove(context!);
		}
	}

	public bool CanDispose
	{
		get
		{
			lock (_lock)
			{
				return _registry.Count == 0;
			}
		}
	}

	protected override void OnDispose()
	{
		lock (_lock)
		{
			var count = _registry.Count;
			Debug.WriteLineIf(count != 0, $"Disposing a ReaderWriterLockTracker with {count} contexts still registered.");
			_registry.Clear();
		}

		var l = Lock;
		Lock = null;
		l?.Dispose();
	}
}