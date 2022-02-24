using System.Threading;

namespace Open.Threading;

/// <summary>
/// <para>
/// A simple write lock class which also allows for a timeout.
/// Extensions are available for use with a ReaderWriterLockSlim.
/// </para>
/// <para>Example:</para>
/// <code>
/// using(readWriteLockSlimInstance.WriteLock())
/// {
///     // do some synchronized work.
/// }
/// </code>
/// ...or...
/// <code>
/// using(readWriteLockSlimInstance.WriteLock(1000)) // If the timeout expires an exception is thrown.
/// {
///     // do some synchronized work.
/// }
/// </code>
/// ...or...
/// <code>
/// using(var syncLock = readWriteLockSlimInstance.WriteLock(1000,false)) // If the timeout expires an exception is thrown.
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
public sealed class WriteLock : LockBase<ReaderWriterLockSlim>
{
	public WriteLock(ReaderWriterLockSlim target, int? millisecondsTimeout = null, bool throwIfTimeout = true)
	: base(target, target.EnterWriteLock(millisecondsTimeout, throwIfTimeout))
	{
	}

	protected override void OnDispose(ReaderWriterLockSlim? target) => target?.ExitWriteLock();
}
