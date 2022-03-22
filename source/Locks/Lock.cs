using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Contracts;

namespace Open.Threading;

/// <remarks>Attempts to acquire an exclusive lock on an object using <see cref="Monitor"/>.</remarks>
/// <inheritdoc cref="ILock" />
public readonly struct Lock : ILock
{
	private readonly object _target;

	/// <inheritdoc cref="ILock.LockHeld" />
	public readonly bool LockHeld;
	bool ILock.LockHeld => LockHeld;

	/// <inheritdoc />
	public LockType LockType
		=> LockType.Monitor;

	/// <inheritdoc />
	public LockType LockTypeHeld
		=> LockHeld ? LockType.Monitor : LockType.None;

	/// <summary>Constructs a <see cref="Lock"/> for use with a <c>using</c> block.</summary>
	/// <param name="target">The object to acquire an exclusive lock for.</param>
	/// <param name="timeout">
	/// <para>Indicates for how long a timeout should be used to acquire a lock.<br/><c>default</c> or <c>-1</c> will wait indefinitely.</para>
	/// <para>Can also be a value of <see cref="TimeSpan"/>, <see cref="int"/>, <see cref="long"/>, or <see cref="double"/>. (Implicit conversion.)</para>
	/// </param>
	/// <param name="throwIfTimeout">
	/// If <b>true</b> (default), a <see cref="TimeoutException"/> exception
	/// will be thrown if a lock cannot be acquired within the timeout.
	/// If <b>false</b> and no lock could be acquired, the .LockHeld value will be false.
	/// </param>
	public Lock(
		object target,
		LockTimeout timeout = default,
		bool throwIfTimeout = true)
	{
		_target = AssertSyncObject(target);
		if (timeout.IsFinite)
		{
			LockHeld = Monitor.TryEnter(target, timeout);
			if (throwIfTimeout && !LockHeld) timeout.Throw(LockType.Monitor);
			return;
		}

		Monitor.Enter(target);
		LockHeld = true;
	}

	/// <summary>
	/// Returns true if <paramref name="syncObject"/> is valid for locking.
	/// </summary>
	public static bool IsValidSyncObject(object? syncObject)
		=> syncObject switch // Avoid the lock object being immutable...
		{
			null or string or Type or ValueType => false,
			_ => true,
		};

	/// <summary>
	/// Throws an exception if <paramref name="syncObject"/> is not valid for locking.
	/// </summary>
	/// <exception cref="ArgumentNullException">If <paramref name="syncObject"/> is null.</exception>
	/// <exception cref="ArgumentException">If <paramref name="syncObject"/> is not valid for locking.</exception>
#if NETSTANDARD2_1_OR_GREATER
	[return: NotNull]
#endif
	public static object AssertSyncObject(object? syncObject)
	{
		if (syncObject is null)
			throw new ArgumentNullException(nameof(syncObject));
		if (syncObject is string or Type or ValueType)
			throw new ArgumentException($"Is not valid sync object. Invalid type: ({syncObject.GetType()})", nameof(syncObject));
		Contract.EndContractBlock();
		return syncObject;
	}

	/// <inheritdoc cref="ILock.LockHeld"/>
	[ExcludeFromCodeCoverage]
	public static implicit operator bool(Lock monitor) => monitor.LockHeld;

	/// <inheritdoc cref="ILock.LockTypeHeld"/>
	[ExcludeFromCodeCoverage]
	public static implicit operator LockType(Lock monitor) => monitor.LockTypeHeld;

	/// <summary>Releases the lock if one was acquired.</summary>
	/// <remarks>Should only be called once.  Calling more than once may produce unexpected results.</remarks>
	public void Dispose()
	{
		if(LockHeld) Monitor.Exit(_target);
	}
}
