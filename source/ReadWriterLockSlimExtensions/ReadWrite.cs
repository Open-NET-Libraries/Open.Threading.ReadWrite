/*!
 * @author electricessence / https://github.com/electricessence/
 * Some portions of this code are based upon code from Stephen Cleary's Nitro library.
 */

using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Contracts;

namespace Open.Threading;

public static partial class ReaderWriterLockSlimExensions
{
	/// <inheritdoc cref="ReadWriteConditional{T}(ReaderWriterLockSlim, ref T, Func{bool, bool}, Func{T})"/>
	[ExcludeFromCodeCoverage]
	public static bool ReadWriteConditional(
		this ReaderWriterLockSlim target,
		Func<bool, bool> condition,
		Action action)
		=> ReadWriteConditional(target, default, condition, action);

	/// <inheritdoc cref="ReadWriteConditional{T}(ReaderWriterLockSlim, LockTimeout, ref T, Func{bool, bool}, Func{T})"/>
	public static bool ReadWriteConditional(
		this ReaderWriterLockSlim target,
		LockTimeout timeout,
		Func<bool, bool> condition,
		Action action)
	{
		if (condition is null) throw new ArgumentNullException(nameof(condition));
		if (action is null) throw new ArgumentNullException(nameof(action));
		Contract.EndContractBlock();

		using (var readLock = new ReadLock(target, timeout))
		{
			if (!condition(false)) return false;
		}

		using var upgradableLock = new UpgradableReadLock(target, timeout);
		if (!condition(true)) return false;

		using var writeLock = new WriteLock(target, timeout);
		action();
		return true;
	}

	/// <summary>
	/// Acquires a <b>write</b> lock from the <paramref name="target"/> before invoking the <paramref name="action"/>.
	/// </summary>
	/// <remarks>
	/// Starts by testing the <paramref name="condition"/> within a <b>read</b> lock, passing a value of <b>false</b> as the parameter.
	/// If the <paramref name="condition"/> returns <b>true</b>, the lock is released, an <b>upgradable read</b> lock is acquired, and the <paramref name="condition"/> is tested a second time with a value of <b>true</b> as the parameter.
	/// If the <paramref name="condition"/> then returns <b>true</b>, the lock is upgraded to <b>write</b> and the <paramref name="action"/> is executed.<br/>
	/// </remarks>
	/// <inheritdoc cref="WriteConditional{T}(ReaderWriterLockSlim, ref T, Func{bool, bool}, Func{T})"/>
	[ExcludeFromCodeCoverage]
	public static bool ReadWriteConditional<T>(
		this ReaderWriterLockSlim target,
		ref T result,
		Func<bool, bool> condition,
		Func<T> action)
		=> ReadWriteConditional(target, default, ref result, condition, action);

	/// <exception cref="TimeoutException"><inheritdoc cref="Write(ReaderWriterLockSlim, LockTimeout, Action)" path="/exception"/></exception>
	/// <inheritdoc cref="ReadWriteConditional{T}(ReaderWriterLockSlim, ref T, Func{bool, bool}, Func{T})" />
	public static bool ReadWriteConditional<T>(
		this ReaderWriterLockSlim target,
		LockTimeout timeout,
		ref T result,
		Func<bool, bool> condition,
		Func<T> action)
	{
		if (condition is null) throw new ArgumentNullException(nameof(condition));
		if (action is null) throw new ArgumentNullException(nameof(action));
		Contract.EndContractBlock();

		using (var readLock = new ReadLock(target, timeout))
		{
			if (!condition(false)) return false;
		}

		using var upgradableLock = new UpgradableReadLock(target, timeout);
		if (!condition(true)) return false;

		using var writeLock = new WriteLock(target, timeout);
		result = action();
		return true;
	}

	/// <inheritdoc cref="TryReadWriteConditional{T}(ReaderWriterLockSlim, LockTimeout, ref T, Func{bool, bool}, Func{T})" />
	public static bool TryReadWriteConditional(
		this ReaderWriterLockSlim target,
		LockTimeout timeout,
		Func<bool, bool> condition,
		Action action)
	{
		if (condition is null) throw new ArgumentNullException(nameof(condition));
		if (action is null) throw new ArgumentNullException(nameof(action));
		Contract.EndContractBlock();

		using (var readLock = new ReadLock(target, timeout, false))
		{
			if (!readLock.LockHeld || !condition(false)) return false;
		}

		using var upgradableLock = new UpgradableReadLock(target, timeout, false);
		if (!upgradableLock.LockHeld || !condition(true)) return false;

		using var writeLock = new WriteLock(target, timeout, false);
		if (!writeLock.LockHeld) return false;

		action();
		return true;
	}

	/// <remarks><inheritdoc cref="ReadWriteConditional{T}(ReaderWriterLockSlim, LockTimeout, ref T, Func{bool, bool}, Func{T})" path="/remarks[1]"/></remarks>
	/// <inheritdoc cref="TryWriteConditional{T}(ReaderWriterLockSlim, LockTimeout, ref T, Func{bool, bool}, Func{T})" />
	public static bool TryReadWriteConditional<T>(
		this ReaderWriterLockSlim target,
		LockTimeout timeout,
		ref T result,
		Func<bool, bool> condition,
		Func<T> action)
	{
		if (condition is null) throw new ArgumentNullException(nameof(condition));
		if (action is null) throw new ArgumentNullException(nameof(action));
		Contract.EndContractBlock();

		using (var readLock = new ReadLock(target, timeout, false))
		{
			if (!readLock.LockHeld || !condition(false)) return false;
		}

		using var upgradableLock = new UpgradableReadLock(target, timeout, false);
		if (!upgradableLock.LockHeld || !condition(true)) return false;

		using var writeLock = new WriteLock(target, timeout, false);
		if (!writeLock.LockHeld) return false;

		result = action();
		return true;
	}

	/// <summary>
	/// If <paramref name="getValue"/> returns null, the value is acquired from <paramref name="createValue"/>.
	/// </summary>
	/// <typeparam name="T">The return type.</typeparam>
	/// <param name="target">The <see cref="ReaderWriterLockSlim"/> to acquire a lock from.</param>
	/// <param name="getValue">The function to get the value.</param>
	/// <param name="createValue">The create value factory.</param>
	/// <returns>The value acquired.</returns>
	[ExcludeFromCodeCoverage]
	public static T GetOrCreateValue<T>(
		this ReaderWriterLockSlim target,
		Func<T?> getValue,
		Func<T> createValue)
		=> GetOrCreateValue(target, default, getValue, createValue);

	/// <summary>
	/// If <paramref name="getValue"/> returns null, the value is acquired from <paramref name="createValue"/>.
	/// </summary>
	/// <typeparam name="T">The return type.</typeparam>
	/// <param name="target">The <see cref="ReaderWriterLockSlim"/> to acquire a lock from.</param>
	/// <param name="timeout"><inheritdoc cref="Lock(object, LockTimeout, bool)" path="/param[@name='timeout']"/></param>
	/// <param name="getValue">The function to get the value.</param>
	/// <param name="createValue">The create value factory.</param>
	/// <returns>The value acquired.</returns>
	/// <exception cref="TimeoutException"><inheritdoc cref="Write(ReaderWriterLockSlim, LockTimeout, Action)" path="/exception"/></exception>
	public static T GetOrCreateValue<T>(
		this ReaderWriterLockSlim target,
		LockTimeout timeout,
		Func<T?> getValue,
		Func<T> createValue)
	{
		if (getValue is null) throw new ArgumentNullException(nameof(getValue));
		if (createValue is null) throw new ArgumentNullException(nameof(createValue));
		Contract.EndContractBlock();

		T? result = default;
		ReadWriteConditional<T?>(target,
			timeout,
			ref result,
			_ => (result = getValue()) is null,
			createValue);

		return result!;
	}
}
