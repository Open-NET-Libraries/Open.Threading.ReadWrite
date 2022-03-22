using System.Diagnostics.CodeAnalysis;

namespace Open.Threading;

/// <remarks>Attempts to acquire a <b>read</b> lock on a <see cref="ReaderWriterLockSlim"/>.</remarks>
/// <inheritdoc cref="ILock" />
public readonly struct ReadLock : ILock
{
	private readonly ReaderWriterLockSlim _target;

	/// <inheritdoc cref="ILock.LockHeld" />
	public readonly bool LockHeld;
	bool ILock.LockHeld	=> LockHeld;

	/// <inheritdoc />
	public LockType LockType
		=> LockType.Read;

	/// <inheritdoc />
	public LockType LockTypeHeld
		=> LockHeld ? LockType.Read : LockType.None;

	/// <summary>Constructs a <see cref="ReadLock"/> for use with a <see langword="using"/> block.</summary>
	/// <param name="target">The <see cref="ReaderWriterLockSlim"/> to acquire a lock from.</param>
	/// <param name="timeout"><inheritdoc cref="Lock(object, LockTimeout, bool)" path="/param[@name='timeout']"/></param>
	/// <param name="throwIfTimeout"><inheritdoc cref="Lock(object, LockTimeout, bool)" path="/param[@name='throwIfTimeout']"/></param>
	/// <inheritdoc cref="Lock(object, LockTimeout, bool)"/>
	public ReadLock(
		ReaderWriterLockSlim target,
		LockTimeout timeout = default,
		bool throwIfTimeout = true)
	{
		_target = target ?? throw new ArgumentNullException(nameof(target));
		LockHeld = target.TryEnterReadLock(timeout)
			|| throwIfTimeout && timeout.Throw(LockType.Read);
	}

	/// <inheritdoc cref="ILock.LockHeld"/>
	[ExcludeFromCodeCoverage]
	public static implicit operator bool(ReadLock readLock) => readLock.LockHeld;

	/// <inheritdoc cref="ILock.LockTypeHeld"/>
	[ExcludeFromCodeCoverage]
	public static implicit operator LockType(ReadLock readLock) => readLock.LockTypeHeld;

	/// <inheritdoc cref="Lock.Dispose" />
	public void Dispose()
	{
		if (LockHeld) _target.ExitReadLock();
	}
}
