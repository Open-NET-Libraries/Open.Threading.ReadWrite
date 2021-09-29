using System;
using System.Threading;

namespace Open.Threading;

/// <summary>
/// A simple upgragable read lock class which also allows for a timeout.
/// Extensions are available for use with a ReaderWriterLockSlim.
/// 
/// Example:
/// <code>
/// using(readWriteLockSlimInstance.WriteLock())
/// {
///     // do some synchronized work.
/// }
/// </code>
/// ...or...
/// <code>
/// using(readWriteLockSlimInstance.UpgradableReadLock(1000)) // If the timeout expires an exception is thrown.
/// {
///     // do some synchronized work.
/// }
/// </code>
/// ...or...
/// <code>
/// using(var syncLock = readWriteLockSlimInstance.UpgradableReadLock(1000,false)) // If the timeout expires an exception is thrown.
/// {
///     if(!syncLock.LockHeld)
///     {
///         // Do some unsynchronized work.
///     }
///     {
///         // do some synchronized work.
///     }
/// }
/// </code>
/// </summary>
public sealed class UpgradableReadLock : LockBase<ReaderWriterLockSlim>
{
	readonly object _sync = new();
	WriteLock? _upgraded;
	public UpgradableReadLock(ReaderWriterLockSlim target, int? millisecondsTimeout = null, bool throwIfTimeout = true)
	: base(target, target.EnterUpgradeableReadLock(millisecondsTimeout, throwIfTimeout))
	{
	}

	// A useful utility but it's completely fine to create your own write lock under an upgradable one.
	public void UpgradeToWriteLock(int? millisecondsTimeout = null)
	{
		if (_target is null)
			throw new InvalidOperationException("Cannot upgrade a lock when the lock was not held.");
		lock (_sync)
		{
			if (_upgraded is not null)
				throw new InvalidOperationException("A write lock is already in effect.");
			_upgraded = new WriteLock(_target, millisecondsTimeout);
		}
	}

	public void Downgrade()
	{
		if (!LockHeld)
			throw new InvalidOperationException("Cannot upgrade a lock when the lock was not held.");
		lock (_sync)
		{
			if (_upgraded is null)
				throw new InvalidOperationException("There is no write lock in effect to downgrade from.");
			_upgraded.Dispose();
			_upgraded = null;
		}
	}

	protected override void OnDispose(ReaderWriterLockSlim? target)
	{
		lock (_sync) Interlocked.Exchange(ref _upgraded, null)?.Dispose();
		target?.ExitUpgradeableReadLock();
	}
}
