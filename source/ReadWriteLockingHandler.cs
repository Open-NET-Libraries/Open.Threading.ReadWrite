using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Open.Threading;

/// <inheritdoc />
[ExcludeFromCodeCoverage]
public readonly record struct ReadWriteLockingHandler : IReadWriteLockingHandler<ReaderWriterLockSlim>
{
	/// <inheritdoc />
	public ReaderWriterLockSlim Sync { get; }

	/// <inheritdoc />
	public ReadWriteLockingHandler(ReaderWriterLockSlim rwlock)
	{
		Sync = rwlock ?? throw new ArgumentNullException(nameof(rwlock));
	}

	/// <inheritdoc />
	public bool Try(LockType lockType, LockTimeout timeout, Action action, bool throwIfTimeout = false)
	{
		using var iLock = Sync.GetLock(lockType, timeout, throwIfTimeout);
		if (!iLock.LockHeld) return false;
		action();
		return true;
	}

	/// <inheritdoc />
	public bool Try<T>(LockType lockType, LockTimeout timeout, out T result, Func<T> action, bool throwIfTimeout = false)
	{
		using var iLock = Sync.GetLock(lockType, timeout, throwIfTimeout);
		if (!iLock.LockHeld)
		{
			result = default!;
			return false;
		}
		result = action();
		return true;
	}

	/// <inheritdoc />
	public bool Try(LockType lockType, LockTimeout timeout, Action<ReaderWriterLockSlim> action, bool throwIfTimeout = false)
	{
		using var iLock = Sync.GetLock(lockType, timeout, throwIfTimeout);
		if (!iLock.LockHeld) return false;
		action(Sync);
		return true;
	}

	/// <inheritdoc />
	public bool Try<T>(LockType lockType, LockTimeout timeout, out T result, Func<ReaderWriterLockSlim, T> action, bool throwIfTimeout = false)
	{
		using var iLock = Sync.GetLock(lockType, timeout, throwIfTimeout);
		if (!iLock.LockHeld)
		{
			result = default!;
			return false;
		}
		result = action(Sync);
		return true;
	}

	/// <inheritdoc />
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public bool TryRead(LockTimeout timeout, Action action, bool throwIfTimeout = false)
		=> Sync.TryRead(timeout, action, throwIfTimeout);

	/// <inheritdoc />
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public bool TryRead<T>(LockTimeout timeout, out T result, Func<T> action, bool throwIfTimeout = false)
		=> Sync.TryRead(timeout, out result, action, throwIfTimeout);

	/// <inheritdoc />
	[ExcludeFromCodeCoverage]
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public bool TryReadUpgradable(LockTimeout timeout, Action<ReaderWriterLockSlim> action, bool throwIfTimeout = false)
		=> Sync.TryReadUpgradable(timeout, action, throwIfTimeout);

	/// <inheritdoc />
	[ExcludeFromCodeCoverage]
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public bool TryReadUpgradable<T>(LockTimeout timeout, out T result, Func<ReaderWriterLockSlim, T> action, bool throwIfTimeout = false)
		=> Sync.TryReadUpgradable(timeout, out result, action, throwIfTimeout);

	/// <inheritdoc />
	[ExcludeFromCodeCoverage]
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public bool TryReadUpgradable(LockTimeout timeout, Action action, bool throwIfTimeout = false)
		=> Sync.TryReadUpgradable(timeout, action, throwIfTimeout);

	/// <inheritdoc />
	[ExcludeFromCodeCoverage]
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public bool TryReadUpgradable<T>(LockTimeout timeout, out T result, Func<T> action, bool throwIfTimeout = false)
		=> Sync.TryReadUpgradable(timeout, out result, action, throwIfTimeout);

	/// <inheritdoc />
	[ExcludeFromCodeCoverage]
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public bool TryWrite(LockTimeout timeout, Action action, bool throwIfTimeout = false)
		=> Sync.TryWrite(timeout, action, throwIfTimeout);

	/// <inheritdoc />
	[ExcludeFromCodeCoverage]
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public bool TryWrite<T>(LockTimeout timeout, out T result, Func<T> action, bool throwIfTimeout = false)
		=> Sync.TryWrite(timeout, out result, action, throwIfTimeout);
}
