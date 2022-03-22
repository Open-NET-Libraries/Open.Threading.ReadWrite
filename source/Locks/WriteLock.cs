using System.Diagnostics.CodeAnalysis;

namespace Open.Threading;

/// <remarks>Attempts to acquire am <b>upgradable read</b> lock on a <see cref="ReaderWriterLockSlim"/>.</remarks>
/// <inheritdoc cref="ILock" />
public readonly struct WriteLock : ILock
{
	private readonly ReaderWriterLockSlim _target;

	/// <inheritdoc cref="ILock.LockHeld" />
	public readonly bool LockHeld;
	bool ILock.LockHeld	=> LockHeld;

	/// <inheritdoc />
	public LockType LockType
		=> LockType.Write;

	/// <inheritdoc />
	public LockType LockTypeHeld
		=> LockHeld ? LockType.Write : LockType.None;

	/// <summary>Constructs a <see cref="WriteLock"/> for use with a <see langword="using"/> block.</summary>
	/// <inheritdoc cref="ReadLock(ReaderWriterLockSlim, LockTimeout, bool)"/>
	public WriteLock(
		ReaderWriterLockSlim target,
		LockTimeout timeout = default,
		bool throwIfTimeout = true)
	{
		_target = target ?? throw new ArgumentNullException(nameof(target));
		LockHeld = target.TryEnterWriteLock(timeout)
			|| throwIfTimeout && timeout.Throw(LockType.Write);
	}

	/// <inheritdoc cref="ILock.LockHeld"/>
	[ExcludeFromCodeCoverage]
	public static implicit operator bool(WriteLock writeLock) => writeLock.LockHeld;

	/// <inheritdoc cref="ILock.LockTypeHeld"/>
	[ExcludeFromCodeCoverage]
	public static implicit operator LockType(WriteLock writeLock) => writeLock.LockTypeHeld;

	/// <inheritdoc cref="Lock.Dispose" />
	public void Dispose()
	{
		if(LockHeld) _target.ExitWriteLock();
	}
}
