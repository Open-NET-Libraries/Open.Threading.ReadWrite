/*!
 * @author electricessence / https://github.com/electricessence/
 * Some portions of this code are based upon code from Stephen Cleary's Nitro library.
 */

using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Contracts;

namespace Open.Threading;

/// <summary>
/// A collection of extensions for simplifying <b>read</b> / <b>write</b> operations with a <see cref="ReaderWriterLockSlim"/>.
/// </summary>
public static partial class ReaderWriterLockSlimExensions
{
	/// <summary>
	/// Extension for checking lock status... Should only be used for debugging.
	/// </summary>
	/// <returns>true if not in a locked state; otherwise false.</returns>
	[ExcludeFromCodeCoverage]
	public static bool IsLockFree(this ReaderWriterLockSlim target)
	{
		if (target is null)
			throw new ArgumentNullException(nameof(target));
		Contract.EndContractBlock();

		return target.CurrentReadCount == 0
			&& target.RecursiveReadCount == 0
			&& target.RecursiveUpgradeCount == 0
			&& target.RecursiveWriteCount == 0
			&& target.WaitingReadCount == 0
			&& target.WaitingWriteCount == 0
			&& target.WaitingUpgradeCount == 0
			&& !target.IsReadLockHeld
			&& !target.IsUpgradeableReadLockHeld
			&& !target.IsWriteLockHeld;
	}

	internal static bool Throw(this LockTimeout timeout, LockType lockType)
		=> throw new TimeoutException($"Could not acquire {lockType} lock within the timeout specified. (timeout = {timeout.Milliseconds} ms)");

	/// <param name="target"><inheritdoc cref="ReadLock.ReadLock(ReaderWriterLockSlim, LockTimeout, bool)" path="/param[@name='target'][1]"/></param>
	/// <param name="timeout"><inheritdoc cref="ReadLock.ReadLock(ReaderWriterLockSlim, LockTimeout, bool)" path="/param[@name='timeout'][1]"/></param>
	/// <exception cref="TimeoutException">If the timeout was reached and no lock could be acquired.</exception>
	/// <inheritdoc cref="ReaderWriterLockSlim.EnterReadLock()"/>
	public static void EnterReadLock(
		this ReaderWriterLockSlim target,
		LockTimeout timeout)
	{
		if (target is null) throw new ArgumentNullException(nameof(target));
		Contract.EndContractBlock();

		if (timeout.IsFinite)
		{
			if (!target.TryEnterReadLock(timeout))
				timeout.Throw(LockType.Read);
			return;
		}

		target.EnterReadLock();
	}

	/// <summary><inheritdoc cref="ReaderWriterLockSlim.EnterUpgradeableReadLock" path="/summary"/></summary>
	/// <inheritdoc cref="EnterReadLock(ReaderWriterLockSlim, LockTimeout)"/>
	public static void EnterUpgradeableReadLock(
		this ReaderWriterLockSlim target,
		LockTimeout timeout)
	{
		if (target is null) throw new ArgumentNullException(nameof(target));
		Contract.EndContractBlock();

		if (timeout.IsFinite)
		{
			if (!target.TryEnterUpgradeableReadLock(timeout))
				timeout.Throw(LockType.UpgradableRead);
			return;
		}

		target.EnterUpgradeableReadLock();
	}

	/// <summary><inheritdoc cref="ReaderWriterLockSlim.EnterWriteLock" path="/summary"/></summary>
	/// <inheritdoc cref="EnterReadLock(ReaderWriterLockSlim, LockTimeout)"/>
	public static void EnterWriteLock(
		this ReaderWriterLockSlim target,
		LockTimeout timeout)
	{
		if (target is null) throw new ArgumentNullException(nameof(target));
		Contract.EndContractBlock();

		if (timeout.IsFinite)
		{
			if (!target.TryEnterWriteLock(timeout))
				timeout.Throw(LockType.Write);
			return;
		}

		target.EnterWriteLock();
	}

	/// <summary>Constructs a <see cref="ILock"/> of the <paramref name="lockType"/> requested for use with a <c>using</c> block.</summary>
	/// <param name="target">The <see cref="ReaderWriterLockSlim"/> to acquire a lock from.</param>
	/// <param name="lockType">The <see cref="LockType"/> to acquire.</param>
	/// <param name="timeout"><inheritdoc cref="Lock(object, LockTimeout, bool)" path="/param[@name='timeout']"/></param>
	/// <param name="throwIfTimeout"><inheritdoc cref="Lock(object, LockTimeout, bool)" path="/param[@name='throwIfTimeout']"/></param>
	/// <returns>An <see cref="ILock"/> of the type requestd.</returns>
	public static ILock GetLock(
		this ReaderWriterLockSlim target,
		LockType lockType,
		LockTimeout timeout = default,
		bool throwIfTimeout = true) => lockType switch
		{
			LockType.Read => new ReadLock(target, timeout, throwIfTimeout),
			LockType.UpgradableRead => new UpgradableReadLock(target, timeout, throwIfTimeout),
			LockType.Write => new WriteLock(target, timeout, throwIfTimeout),
			_ => throw new ArgumentOutOfRangeException(nameof(lockType), lockType, "Must be either Read, ReadUpgradable, or Write.")
		};

	/// <remarks>Only returns a lock if one is actually held.</remarks>
	/// <inheritdoc cref="GetLock(ReaderWriterLockSlim, LockType, LockTimeout, bool)"/>
	public static ILock? TryGetLock(
		this ReaderWriterLockSlim target,
		LockType lockType,
		LockTimeout timeout)
	{
		var iLock = GetLock(target, lockType, timeout, false);
		if (iLock.LockHeld) return iLock;
		iLock.Dispose();
		return null;
	}

	/// <exception cref="TimeoutException">If no lock could be acquired within the timeout.</exception>
	/// <inheritdoc cref="ReadLock.ReadLock(ReaderWriterLockSlim, LockTimeout, bool)"/>
	[ExcludeFromCodeCoverage]
	public static ReadLock ReadLock(
		this ReaderWriterLockSlim target,
		LockTimeout timeout)
		=> new(target, timeout);

	/// <inheritdoc cref="ReadLock.ReadLock(ReaderWriterLockSlim, LockTimeout, bool)"/>
	[ExcludeFromCodeCoverage]
	public static ReadLock ReadLock(this ReaderWriterLockSlim target)
		=> new(target);

	/// <inheritdoc cref="ReadLock(ReaderWriterLockSlim, LockTimeout)" path="/exception"/>
	/// <inheritdoc cref="UpgradableReadLock.UpgradableReadLock(ReaderWriterLockSlim, LockTimeout, bool)"/>
	[ExcludeFromCodeCoverage]
	public static UpgradableReadLock UpgradableReadLock(
		this ReaderWriterLockSlim target,
		LockTimeout timeout = default)
		=> new(target, timeout);

	/// <inheritdoc cref="ReadLock(ReaderWriterLockSlim, LockTimeout)" path="/exception"/>
	/// <inheritdoc cref="WriteLock.WriteLock(ReaderWriterLockSlim, LockTimeout, bool)"/>
	public static WriteLock WriteLock(
		this ReaderWriterLockSlim target,
		LockTimeout timeout = default)
		=> new(target, timeout);
}
