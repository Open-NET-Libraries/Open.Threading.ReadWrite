using Open.Disposable;
using System.Diagnostics;

namespace Open.Threading;

class ReaderWriterLockTracker : DisposableBase
{
	readonly HashSet<object> _registry = new();

	public ReaderWriterLockSlim? Lock;

	private ReaderWriterLockTracker(ReaderWriterLockSlim rwlock)
		=> Lock = rwlock;

	public ReaderWriterLockTracker(LockRecursionPolicy policy)
		: this(new ReaderWriterLockSlim(policy)) { }

	public ReaderWriterLockSlim? Reserve(object context)
	{
		lock (_registry)
		{
			var reserved = Lock;
			if (reserved is null || WasDisposed) return null;
			_registry.Add(context);
			return reserved;
		}
	}

	private static bool TryAcquireLock(
		ReaderWriterLockSlim target,
		LockType type,
		int timeout)
	{
		Debug.Assert(target is not null);
		return type switch
		{
			LockType.Read => target!.TryEnterReadLock(timeout),
			LockType.UpgradableRead => target!.TryEnterUpgradeableReadLock(timeout),
			LockType.Write => target!.TryEnterWriteLock(timeout),
			_ => false,
		};
	}

	private static bool TryAcquireLock(ReaderWriterLockSlim target, LockType type, int timeout, bool throwIfTimeout)
	{
		if (!throwIfTimeout)
			return TryAcquireLock(target, type, timeout);

		AcquireLock(target, type, timeout);
		return true;
	}

	private static void AcquireLock(ReaderWriterLockSlim target, LockType type, int timeout)
	{
		Debug.Assert(target is not null);
		switch (type)
		{
			case LockType.Read:
				target!.EnterReadLock(timeout);
				break;
			case LockType.UpgradableRead:
				target!.EnterUpgradeableReadLock(timeout);
				break;
			case LockType.Write:
				target!.EnterWriteLock(timeout);
				break;
			default:
				throw new ArgumentOutOfRangeException(nameof(type));
		}
	}

	private void ReleaseLock(ReaderWriterLockSlim target, LockType type)
	{
		Debug.Assert(target is not null);
		switch (type)
		{
			case LockType.Read:
				target!.ExitReadLock();
				break;
			case LockType.UpgradableRead:
				target!.ExitUpgradeableReadLock();
				break;
			case LockType.Write:
				target!.ExitWriteLock();
				break;
			default:
				throw new ArgumentOutOfRangeException(nameof(type));
		}

	}


	public ILock? TryGetLock(object context, LockType type, int timeout, bool throwIfTimeout)
	{
		var lockHeld = false;
		var reserved = Reserve(context);
		if (reserved is null) return null; // could not acquire a lock.
		try
		{
			var iLock = reserved.GetLock(type, timeout, throwIfTimeout);
			lockHeld = iLock.LockHeld;
			if (lockHeld) return iLock;

			
			{

			}
			return lockHeld ? iLock : null;
		}
		catch (LockRecursionException lrex)
		{
			Debug.WriteLine(lrex.ToString());
			Debugger.Break(); // Need to be able to track down source.
			throw;
		}
		finally
		{
			if (!lockHeld) Clear(context);
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
			var count = _registry.Count;
			Debug.WriteLineIf(count != 0, $"Disposing a ReaderWriterLockTracker with {count} contexts still registered.");
			_registry.Clear();
		}

		var l = Lock;
		Lock = null;
		l?.Dispose();
	}
}