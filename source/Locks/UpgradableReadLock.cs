using System.Diagnostics.CodeAnalysis;

namespace Open.Threading;

/// <remarks>Attempts to acquire am <b>upgradable read</b> lock on a <see cref="ReaderWriterLockSlim"/>.</remarks>
/// <inheritdoc cref="ILock" />
public readonly struct UpgradableReadLock : ILock
{
	private readonly ReaderWriterLockSlim _target;

	/// <inheritdoc cref="ILock.LockHeld" />
	public readonly bool LockHeld;
	bool ILock.LockHeld	=> LockHeld;

	/// <inheritdoc />
	public LockType LockType
		=> LockType.UpgradableRead;

	/// <inheritdoc />
	public LockType LockTypeHeld
		=> LockHeld ? LockType.UpgradableRead : LockType.None;

	/// <summary>Constructs an <see cref="UpgradableReadLock"/> for use with a <see langword="using"/> block.</summary>
	/// <inheritdoc cref="ReadLock(ReaderWriterLockSlim, LockTimeout, bool)"/>
	public UpgradableReadLock(
		ReaderWriterLockSlim target,
		LockTimeout timeout = default,
		bool throwIfTimeout = true)
	{
		_target = target ?? throw new ArgumentNullException(nameof(target));
		LockHeld = target.TryEnterUpgradeableReadLock(timeout)
			|| throwIfTimeout && timeout.Throw(LockType.UpgradableRead);
	}

	/// <inheritdoc cref="ILock.LockHeld"/>
	[ExcludeFromCodeCoverage]
	public static implicit operator bool(UpgradableReadLock readLock) => readLock.LockHeld;

	/// <inheritdoc cref="ILock.LockTypeHeld"/>
	[ExcludeFromCodeCoverage]
	public static implicit operator LockType(UpgradableReadLock readLock) => readLock.LockTypeHeld;

	/// <inheritdoc cref="Lock.Dispose" />
	public void Dispose()
	{
		if(LockHeld) _target.ExitUpgradeableReadLock();
	}
}
