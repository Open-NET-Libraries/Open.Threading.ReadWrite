namespace Open.Threading;

/// <summary>
/// A configurable disposable that should be prefixed by a <see langword="using"/> statement.
/// </summary>
public interface ILock : IDisposable
{
	/// <summary>
	/// <see langword="true"/> if a lock was acquired; otherwise <see langword="false"/>.
	/// </summary>
	bool LockHeld { get; }

	/// <summary>
	/// The type of lock that was attempted.
	/// </summary>
	LockType LockType { get; }

	/// <summary>
	/// The type of lock that was acquired.
	/// </summary>
	/// <remarks>
	/// Will be <see cref="LockType.None"/> if unable to acquire a lock (timeout was reached).
	/// </remarks>
	LockType LockTypeHeld { get; }
}
