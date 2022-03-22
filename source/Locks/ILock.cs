namespace Open.Threading;

/// <summary>
/// A configurable disposable that should be prefixed by a <c>using</c> statement.
/// </summary>
public interface ILock : IDisposable
{
	/// <summary>
	/// <b>true</b> if a lock was acquired; otherwise <b>false</b>.
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
