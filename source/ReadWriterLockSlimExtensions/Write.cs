/*!
 * @author electricessence / https://github.com/electricessence/
 * Some portions of this code are based upon code from Stephen Cleary's Nitro library.
 */

using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Contracts;

namespace Open.Threading;

public static partial class ReaderWriterLockSlimExensions
{
	#region Docs
	/// <summary>
	/// Acquires a <b>write</b> lock from the <paramref name="target"/> before invoking the <paramref name="action"/>.
	/// </summary>
	/// <inheritdoc cref="ReadDoc{T}(ReaderWriterLockSlim, LockTimeout, out T, Func{T}, bool)"/>
#pragma warning disable IDE0079 // Remove unnecessary suppression
	[SuppressMessage("Style", "IDE0060:Remove unused parameter")]
	[SuppressMessage("CodeQuality", "IDE0051:Remove unused private members")]
#pragma warning restore IDE0079 // Remove unnecessary suppression
	[ExcludeFromCodeCoverage]
	private static void WriteDoc<T>(
		ReaderWriterLockSlim target,
		LockTimeout timeout,
		out T value,
		Action action,
		bool throwIfTimeout = false) => throw new NotSupportedException();

	/// <remarks>
	/// Starts by testing the <paramref name="condition"/> without a lock, passing a value of <see langword="false"/> as the parameter.<br/>
	/// If the <paramref name="condition"/> returns <see langword="true"/>, an <b>upgradable read</b> lock is acquired and the <paramref name="condition"/> is tested a second time with a value of <see langword="true"/> as the parameter.
	/// If the <paramref name="condition"/> then returns <see langword="true"/>, the lock is upgraded to <b>write</b> and the <paramref name="action"/> is executed.<br/>
	/// </remarks>
	/// <param name="target"><inheritdoc cref="WriteLock.WriteLock(ReaderWriterLockSlim, LockTimeout, bool)" path="/param[@name='target'][1]"/></param>
	/// <param name="timeout"><inheritdoc cref="WriteLock.WriteLock(ReaderWriterLockSlim, LockTimeout, bool)" path="/param[@name='timeout'][1]"/></param>
	/// <param name="condition">
	/// <para>The condition to test before invoking the action.</para>
	/// <para>Will be invoked at least once. A return value of <see langword="false"/> will skip invoking the action.</para>
	/// </param>
	/// <param name="result"><inheritdoc cref="WriteDoc{T}(ReaderWriterLockSlim, LockTimeout, out T, Action, bool)" path="/param[@name='result'][1]"/></param>
	/// <param name="action"><inheritdoc cref="WriteDoc{T}(ReaderWriterLockSlim, LockTimeout, out T, Action, bool)" path="/param[@name='action'][1]"/></param>
	/// <param name="throwIfTimeout"><inheritdoc cref="WriteDoc{T}(ReaderWriterLockSlim, LockTimeout, out T, Action, bool)" path="/param[@name='throwIfTimeout'][1]"/></param>
	/// <returns><see langword="true"/> if the action was invoked and <paramref name="result"/> was updated; otherwise <see langword="false"/>.</returns>
	/// <inheritdoc cref="TryWrite(ReaderWriterLockSlim, LockTimeout, Action, bool)"/>
#pragma warning disable IDE0079 // Remove unnecessary suppression
	[SuppressMessage("Style", "IDE0060:Remove unused parameter")]
	[SuppressMessage("CodeQuality", "IDE0051:Remove unused private members")]
#pragma warning restore IDE0079 // Remove unnecessary suppression
	[ExcludeFromCodeCoverage]
	private static bool TryWriteConditionalDoc<T>(
		this ReaderWriterLockSlim target,
		LockTimeout timeout,
		ref T result,
		Func<bool, bool> condition,
		Func<T> action,
		bool throwIfTimeout = false) => throw new NotImplementedException();
	#endregion

	/// <inheritdoc cref="WriteDoc{T}(ReaderWriterLockSlim, LockTimeout, out T, Action, bool)"/>
	[ExcludeFromCodeCoverage]
	public static void Write(
		this ReaderWriterLockSlim target,
		Action action)
		=> Write(target, default, action);

	/// <remarks>Throws a <see cref="TimeoutException"/> if the timeout is reached and no lock was acquired.</remarks>
	/// <exception cref="TimeoutException">If the timeout is reached and no lock is acquired.</exception>
	/// <inheritdoc cref="WriteDoc{T}(ReaderWriterLockSlim, LockTimeout, out T, Action, bool)"/>
	public static void Write(
		this ReaderWriterLockSlim target,
		LockTimeout timeout,
		Action action)
	{
		if (action is null) throw new ArgumentNullException(nameof(action));
		using var writeLock = new WriteLock(target, timeout);
		action();
	}

	/// <inheritdoc cref="Write(ReaderWriterLockSlim, Action)"/>
	/// <returns><inheritdoc cref="Write{T}(ReaderWriterLockSlim, LockTimeout, Func{T})" path="/returns"/></returns>
	[ExcludeFromCodeCoverage]
	public static T Write<T>(
		this ReaderWriterLockSlim target,
		Func<T> action)
		=> Write(target, default, action);

	/// <returns>The <typeparamref name="T"/> value produced by the action.</returns>
	/// <inheritdoc cref="Write(ReaderWriterLockSlim, LockTimeout, Action)"/>
	public static T Write<T>(
		this ReaderWriterLockSlim target,
		LockTimeout timeout,
		Func<T> action)
	{
		if (action is null) throw new ArgumentNullException(nameof(action));
		using var writeLock = new WriteLock(target, timeout);
		return action();
	}

	/// <summary>Attempts to acquire a <b>write</b> lock within the <paramref name="timeout"/> and invokes the <paramref name="action"/> if a lock is acquired.</summary>
	/// <returns><see langword="true"/> if the action was executed; otherwise <see langword="false"/> because the timeout was reached.</returns>
	/// <inheritdoc cref="WriteDoc{T}(ReaderWriterLockSlim, LockTimeout, out T, Action, bool)"/>
	public static bool TryWrite(
		this ReaderWriterLockSlim target,
		LockTimeout timeout,
		Action action,
		bool throwIfTimeout = false)
	{
		if (action is null)	throw new ArgumentNullException(nameof(action));
		Contract.EndContractBlock();

		using var writeLock = new WriteLock(target, timeout, throwIfTimeout);
		if (writeLock) action();
		return writeLock;
	}

	/// <summary>Attempts to acquire a <b>write</b> lock within the <paramref name="timeout"/> and invokes the <paramref name="action"/> if a lock is acquired.</summary>
	/// <inheritdoc cref="TryRead(ReaderWriterLockSlim, LockTimeout, Action, bool)"/>
	public static bool TryWrite<T>(
		this ReaderWriterLockSlim target,
		LockTimeout timeout,
		out T result,
		Func<T> action,
		bool throwIfTimeout = false)
	{
		if (action is null) throw new ArgumentNullException(nameof(action));
		using var writeLock = new WriteLock(target, timeout, throwIfTimeout);
		result = writeLock ? action() : default!;
		return writeLock;
	}

	/// <inheritdoc cref="WriteConditional{T}(ReaderWriterLockSlim, ref T, Func{bool}, Func{T})"/>
	[ExcludeFromCodeCoverage]
	public static bool WriteConditional(
		this ReaderWriterLockSlim target,
		Func<bool> condition,
		Action action)
		=> WriteConditional(target, default, condition, action);

	/// <inheritdoc cref="WriteConditional{T}(ReaderWriterLockSlim, LockTimeout, ref T, Func{bool}, Func{T})"/>
	public static bool WriteConditional(
		this ReaderWriterLockSlim target,
		LockTimeout timeout,
		Func<bool> condition,
		Action action)
	{
		if (condition is null) throw new ArgumentNullException(nameof(condition));
		if (action is null) throw new ArgumentNullException(nameof(action));
		Contract.EndContractBlock();

		using var upgradableLock = new UpgradableReadLock(target, timeout);
		if (!condition()) return false;

		using var writeLock = new WriteLock(target, timeout);
		action();
		return true;
	}

	/// <summary>
	/// Acquires a <b>write</b> lock from the <paramref name="target"/> before invoking the <paramref name="action"/>.
	/// </summary>
	/// <remarks>
	/// Starts by testing the <paramref name="condition"/> within an <b>upgradable read</b> lock.<br/>
	/// If the <paramref name="condition"/> then returns <see langword="true"/>, the lock is upgraded to <b>write</b> and the <paramref name="action"/> is executed.<br/>
	/// </remarks>
	/// <inheritdoc cref="WriteConditional{T}(ReaderWriterLockSlim, ref T, Func{bool, bool}, Func{T})"/>
	[ExcludeFromCodeCoverage]
	public static bool WriteConditional<T>(
		this ReaderWriterLockSlim target,
		ref T result,
		Func<bool> condition,
		Func<T> action)
		=> WriteConditional(target, default, ref result, condition, action);

	/// <exception cref="TimeoutException"><inheritdoc cref="Write(ReaderWriterLockSlim, LockTimeout, Action)" path="/exception"/></exception>
	/// <inheritdoc cref="WriteConditional{T}(ReaderWriterLockSlim, ref T, Func{bool}, Func{T})" />
	public static bool WriteConditional<T>(
		this ReaderWriterLockSlim target,
		LockTimeout timeout,
		ref T result,
		Func<bool> condition,
		Func<T> action)
	{
		if (condition is null) throw new ArgumentNullException(nameof(condition));
		if (action is null) throw new ArgumentNullException(nameof(action));
		Contract.EndContractBlock();

		using var upgradableLock = new UpgradableReadLock(target, timeout);
		if (!condition()) return false;

		using var writeLock = new WriteLock(target, timeout);
		result = action();
		return true;
	}

	/// <summary><inheritdoc cref="Write{T}(ReaderWriterLockSlim, LockTimeout, Func{T})" path="/summary[1]"/></summary>
	/// <inheritdoc cref="TryWriteConditionalDoc{T}(ReaderWriterLockSlim, LockTimeout, ref T, Func{bool, bool}, Func{T}, bool)"/>
	[ExcludeFromCodeCoverage]
	public static bool WriteConditional(
		this ReaderWriterLockSlim target,
		Func<bool, bool> condition,
		Action action)
		=> WriteConditional(target, default, condition, action);

	/// <summary><inheritdoc cref="Write{T}(ReaderWriterLockSlim, LockTimeout, Func{T})" path="/summary[1]"/></summary>
	/// <exception cref="TimeoutException"><inheritdoc cref="Write(ReaderWriterLockSlim, LockTimeout, Action)" path="/exception"/></exception>
	/// <inheritdoc cref="TryWriteConditional{T}(ReaderWriterLockSlim, LockTimeout, ref T, Func{bool, bool}, Func{T}, bool)"/>
	public static bool WriteConditional(
		this ReaderWriterLockSlim target,
		LockTimeout timeout,
		Func<bool, bool> condition,
		Action action)
	{
		if (condition is null) throw new ArgumentNullException(nameof(condition));
		if (action is null)	throw new ArgumentNullException(nameof(action));
		Contract.EndContractBlock();

		if (!condition(false)) return false;

		using var upgradableLock = new UpgradableReadLock(target, timeout);
		if (!condition(true)) return false;

		using var writeLock = new WriteLock(target, timeout);
		action();
		return true;
	}

	/// <inheritdoc cref="WriteConditional(ReaderWriterLockSlim, Func{bool, bool}, Action)"/>
	[ExcludeFromCodeCoverage]
	public static bool WriteConditional<T>(
		this ReaderWriterLockSlim target,
		ref T result,
		Func<bool, bool> condition,
		Func<T> action)
		=> WriteConditional(target, default, ref result, condition, action);

	/// <inheritdoc cref="WriteConditional(ReaderWriterLockSlim, LockTimeout, Func{bool, bool}, Action)"/>
	public static bool WriteConditional<T>(
		this ReaderWriterLockSlim target,
		LockTimeout timeout,
		ref T result,
		Func<bool, bool> condition,
		Func<T> action)
	{
		if (condition is null) throw new ArgumentNullException(nameof(condition));
		if (action is null)	throw new ArgumentNullException(nameof(action));
		Contract.EndContractBlock();

		if (!condition(false)) return false;

		using var upgradableLock = new UpgradableReadLock(target, timeout);
		if (!condition(true)) return false;

		using var writeLock = new WriteLock(target, timeout);
		result = action();
		return true;
	}

	/// <inheritdoc cref="TryWriteConditional{T}(ReaderWriterLockSlim, LockTimeout, ref T, Func{bool}, Func{T}, bool)"/>
	public static bool TryWriteConditional(
		this ReaderWriterLockSlim target,
		LockTimeout timeout,
		Func<bool> condition,
		Action action,
		bool throwIfTimeout = false)
	{
		if (condition is null)
			throw new ArgumentNullException(nameof(condition));
		if (action is null)
			throw new ArgumentNullException(nameof(action));
		Contract.EndContractBlock();

		using var upgradableLock = new UpgradableReadLock(target, timeout, throwIfTimeout);
		if (!upgradableLock.LockHeld || !condition()) return false;

		using var writeLock = new WriteLock(target, timeout, throwIfTimeout);
		if (!writeLock.LockHeld) return false;

		action();
		return true;
	}

	/// <remarks>
	/// Starts by testing the <paramref name="condition"/> within an <b>upgradable read</b> lock.<br/>
	/// If the <paramref name="condition"/> then returns <see langword="true"/>, the lock is upgraded to <b>write</b> and the <paramref name="action"/> is executed.<br/>
	/// </remarks>
	/// <inheritdoc cref="TryWriteConditionalDoc{T}(ReaderWriterLockSlim, LockTimeout, ref T, Func{bool, bool}, Func{T}, bool)"/>
	public static bool TryWriteConditional<T>(
		this ReaderWriterLockSlim target,
		LockTimeout timeout,
		ref T result,
		Func<bool> condition,
		Func<T> action,
		bool throwIfTimeout = false)
	{
		if (condition is null) throw new ArgumentNullException(nameof(condition));
		if (action is null) throw new ArgumentNullException(nameof(action));
		Contract.EndContractBlock();

		using var upgradableLock = new UpgradableReadLock(target, timeout, throwIfTimeout);
		if (!upgradableLock.LockHeld || !condition()) return false;

		using var writeLock = new WriteLock(target, timeout, throwIfTimeout);
		if (!writeLock.LockHeld) return false;

		result = action();
		return true;
	}

	/// <inheritdoc cref="TryWriteConditional{T}(ReaderWriterLockSlim, LockTimeout, ref T, Func{bool, bool}, Func{T}, bool)"/>
	public static bool TryWriteConditional(
		this ReaderWriterLockSlim target,
		LockTimeout timeout,
		Func<bool, bool> condition,
		Action action,
		bool throwIfTimeout = false)
	{
		if (condition is null)
			throw new ArgumentNullException(nameof(condition));
		if (action is null)
			throw new ArgumentNullException(nameof(action));
		Contract.EndContractBlock();

		if (!condition(false)) return false;

		using var upgradableLock = new UpgradableReadLock(target, timeout, throwIfTimeout);
		if (!upgradableLock.LockHeld || !condition(true)) return false;

		using var writeLock = new WriteLock(target, timeout, throwIfTimeout);
		if (!writeLock.LockHeld) return false;

		action();
		return true;
	}

	/// <inheritdoc cref="TryWriteConditionalDoc{T}(ReaderWriterLockSlim, LockTimeout, ref T, Func{bool, bool}, Func{T}, bool)"/>
	public static bool TryWriteConditional<T>(
		this ReaderWriterLockSlim target,
		LockTimeout timeout,
		ref T result,
		Func<bool, bool> condition,
		Func<T> action,
		bool throwIfTimeout = false)
	{
		if (condition is null) throw new ArgumentNullException(nameof(condition));
		if (action is null) throw new ArgumentNullException(nameof(action));
		Contract.EndContractBlock();

		if (!condition(false)) return false;

		using var upgradableLock = new UpgradableReadLock(target, timeout, throwIfTimeout);
		if (!upgradableLock.LockHeld || !condition(true)) return false;

		using var writeLock = new WriteLock(target, timeout, throwIfTimeout);
		if (!writeLock.LockHeld) return false;

		result = action();
		return true;
	}
}
