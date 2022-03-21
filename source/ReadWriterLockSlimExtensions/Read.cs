/*!
 * @author electricessence / https://github.com/electricessence/
 * Some portions of this code are based upon code from Stephen Cleary's Nitro library.
 */

using System.Diagnostics.CodeAnalysis;

namespace Open.Threading;

public static partial class ReaderWriterLockSlimExensions
{
	#region Docs
	/// <summary>
	/// Acquires a <b>read</b> lock from the <paramref name="target"/> before invoking the <paramref name="action"/>.
	/// </summary>
	/// <param name="target">The <see cref="ReaderWriterLockSlim"/> to acquire a lock from.</param>
	/// <param name="timeout"><inheritdoc cref="Lock(object, LockTimeout, bool)" path="/param[@name='timeout']"/></param>
	/// <param name="result">The <typeparamref name="T"/> value produced by the action.</param>
	/// <param name="action">The action to invoke once a lock is acquired.</param>
	/// <param name="throwIfTimeout">If <b>true</b> and the timeout is reached a <see cref="TimeoutException"/> will be thrown.</param>
#pragma warning disable IDE0079 // Remove unnecessary suppression
	[SuppressMessage("Style", "IDE0060:Remove unused parameter")]
	[SuppressMessage("CodeQuality", "IDE0051:Remove unused private members")]
#pragma warning restore IDE0079 // Remove unnecessary suppression
	[ExcludeFromCodeCoverage]
	private static void ReadDoc<T>(
		ReaderWriterLockSlim target,
		LockTimeout timeout,
		out T result,
		Func<T> action,
		bool throwIfTimeout) => throw new NotSupportedException();
	#endregion

	/// <inheritdoc cref="ReadDoc{T}(ReaderWriterLockSlim, LockTimeout, out T, Func{T}, bool)"/>
	public static void Read(
		this ReaderWriterLockSlim target,
		Action action)
		=> Read(target, default, action);

	/// <remarks>Throws a <see cref="TimeoutException"/> if the timeout is reached and no lock was acquired.</remarks>
	/// <exception cref="TimeoutException">If the timeout is reached and no lock is acquired.</exception>
	/// <inheritdoc cref="ReadDoc{T}(ReaderWriterLockSlim, LockTimeout, out T, Func{T}, bool)"/>
	public static void Read(
		this ReaderWriterLockSlim target,
		LockTimeout timeout,
		Action action)
	{
		if (action is null) throw new ArgumentNullException(nameof(action));
		using var readLock = new ReadLock(target, timeout);
		action();
	}

	/// <returns><inheritdoc cref="Read{T}(ReaderWriterLockSlim, LockTimeout, Func{T})" path="/returns"/></returns>
	/// <inheritdoc cref="Read(ReaderWriterLockSlim, Action)"/>
	public static T Read<T>(
		this ReaderWriterLockSlim target,
		Func<T> action)
		=> Read(target, default, action);

	/// <returns>The <typeparamref name="T"/> value produced by the action.</returns>
	/// <inheritdoc cref="Read(ReaderWriterLockSlim, LockTimeout, Action)"/>
	public static T Read<T>(
		this ReaderWriterLockSlim target,
		LockTimeout timeout,
		Func<T> action)
	{
		if (action is null) throw new ArgumentNullException(nameof(action));
		using var readLock = new ReadLock(target, timeout);
		return action();
	}

	/// <summary>Attempts to acquire a <b>read</b> lock within the <paramref name="timeout"/> and invokes the <paramref name="action"/> if a lock is acquired.</summary>
	/// <returns><b>true</b> if the action was executed; otherwise <b>false</b> because the timeout was reached.</returns>
	/// <inheritdoc cref="ReadDoc{T}(ReaderWriterLockSlim, LockTimeout, out T, Func{T}, bool)"/>
	public static bool TryRead(
		this ReaderWriterLockSlim target,
		LockTimeout timeout,
		Action action,
		bool throwIfTimeout = false)
	{
		if (action is null)	throw new ArgumentNullException(nameof(action));
		using var readLock = new ReadLock(target, timeout, throwIfTimeout);
		if (readLock) action();
		return readLock;
	}

	/// <inheritdoc cref="TryRead(ReaderWriterLockSlim, LockTimeout, Action, bool)"/>
	public static bool TryRead<T>(
		this ReaderWriterLockSlim target,
		LockTimeout timeout,
		out T result,
		Func<T> action,
		bool throwIfTimeout = false)
	{
		if (action is null)	throw new ArgumentNullException(nameof(action));
		using var readLock = new ReadLock(target, timeout, throwIfTimeout);
		result = readLock ? action() : default!;
		return readLock;
	}

	/// <summary>
	/// Acquires an <b>upgradable read</b> lock from the <paramref name="target"/> before invoking the <paramref name="action"/>.
	/// </summary>
	/// <inheritdoc cref="Read(ReaderWriterLockSlim, Action)"/>
	[ExcludeFromCodeCoverage]
	public static void ReadUpgradeable(
		this ReaderWriterLockSlim target,
		Action<ReaderWriterLockSlim> action)
		=> ReadUpgradeable(target, default, action);

	/// <summary><inheritdoc cref="ReadUpgradeable(ReaderWriterLockSlim, Action{ReaderWriterLockSlim})" path="/summary[1]"/></summary>
	/// <inheritdoc cref="Read(ReaderWriterLockSlim, LockTimeout, Action)"/>
	public static void ReadUpgradeable(
		this ReaderWriterLockSlim target,
		LockTimeout timeout,
		Action<ReaderWriterLockSlim> action)
	{
		if (action is null) throw new ArgumentNullException(nameof(action));
		using var readLock = new UpgradableReadLock(target, timeout);
		action(target);
	}

	/// <returns><inheritdoc cref="Read{T}(ReaderWriterLockSlim, Func{T})" path="/returns"/></returns>
	/// <inheritdoc cref="ReadUpgradeable(ReaderWriterLockSlim, Action{ReaderWriterLockSlim})"/>
	[ExcludeFromCodeCoverage]
	public static T ReadUpgradeable<T>(
		this ReaderWriterLockSlim target,
		Func<ReaderWriterLockSlim, T> action)
		=> ReadUpgradeable(target, default, action);

	/// <returns><inheritdoc cref="Read{T}(ReaderWriterLockSlim, LockTimeout, Func{T})" path="/returns"/></returns>
	/// <inheritdoc cref="ReadUpgradeable(ReaderWriterLockSlim, LockTimeout, Action{ReaderWriterLockSlim})"/>
	public static T ReadUpgradeable<T>(
		this ReaderWriterLockSlim target,
		LockTimeout timeout,
		Func<ReaderWriterLockSlim, T> action)
	{
		if (action is null)	throw new ArgumentNullException(nameof(action));
		using var readLock = new UpgradableReadLock(target, timeout);
		return action(target);
	}

	/// <inheritdoc cref="TryReadUpgradable{T}(ReaderWriterLockSlim, LockTimeout, out T, Func{ReaderWriterLockSlim, T}, bool)"/>
	public static bool TryReadUpgradable(
		this ReaderWriterLockSlim target,
		LockTimeout timeout,
		Action<ReaderWriterLockSlim> action,
		bool throwIfTimeout = false)
	{
		if (action is null) throw new ArgumentNullException(nameof(action));
		using var readLock = new UpgradableReadLock(target, timeout, throwIfTimeout);
		if (readLock) action(target);
		return readLock;
	}

	/// <summary><inheritdoc cref="ReadUpgradeable(ReaderWriterLockSlim, Action{ReaderWriterLockSlim})" path="/summary[1]"/></summary>
	/// <inheritdoc cref="TryRead(ReaderWriterLockSlim, LockTimeout, Action, bool)"/>
	public static bool TryReadUpgradable<T>(
		this ReaderWriterLockSlim target,
		LockTimeout timeout,
		out T result,
		Func<ReaderWriterLockSlim, T> action,
		bool throwIfTimeout = false)
	{
		if (action is null) throw new ArgumentNullException(nameof(action));
		using var readLock = new UpgradableReadLock(target, timeout, throwIfTimeout);
		result = readLock ? action(target) : default!;
		return readLock;
	}

	/// <inheritdoc cref="ReadUpgradeable(ReaderWriterLockSlim, Action{ReaderWriterLockSlim})"/>
	[ExcludeFromCodeCoverage]
	public static void ReadUpgradeable(
		this ReaderWriterLockSlim target,
		Action action)
		=> ReadUpgradeable(target, default, action);

	/// <inheritdoc cref="ReadUpgradeable(ReaderWriterLockSlim, LockTimeout, Action{ReaderWriterLockSlim})"/>
	public static void ReadUpgradeable(
		this ReaderWriterLockSlim target,
		LockTimeout timeout,
		Action action)
	{
		if (action is null) throw new ArgumentNullException(nameof(action));
		using var readLock = new UpgradableReadLock(target, timeout);
		action();
	}

	/// <inheritdoc cref="ReadUpgradeable(ReaderWriterLockSlim, LockTimeout, Action{ReaderWriterLockSlim})"/>
	[ExcludeFromCodeCoverage]
	public static T ReadUpgradeable<T>(
		this ReaderWriterLockSlim target,
		Func<T> action)
		=> ReadUpgradeable(target, default, action);

	/// <inheritdoc cref="ReadUpgradeable{T}(ReaderWriterLockSlim, LockTimeout, Func{ReaderWriterLockSlim, T})"/>
	public static T ReadUpgradeable<T>(
		this ReaderWriterLockSlim target,
		LockTimeout timeout,
		Func<T> action)
	{
		if (action is null) throw new ArgumentNullException(nameof(action));
		using var readLock = new UpgradableReadLock(target, timeout);
		return action();
	}

	/// <inheritdoc cref="TryReadUpgradable{T}(ReaderWriterLockSlim, LockTimeout, out T, Func{ReaderWriterLockSlim, T}, bool)"/>
	public static bool TryReadUpgradable(
		this ReaderWriterLockSlim target,
		LockTimeout timeout,
		Action action,
		bool throwIfTimeout = false)
	{
		if (action is null) throw new ArgumentNullException(nameof(action));
		using var readLock = new UpgradableReadLock(target, timeout, throwIfTimeout);
		if (readLock) action();
		return readLock;
	}

	/// <inheritdoc cref="TryReadUpgradable{T}(ReaderWriterLockSlim, LockTimeout, out T, Func{ReaderWriterLockSlim, T}, bool)"/>
	public static bool TryReadUpgradable<T>(
		this ReaderWriterLockSlim target,
		LockTimeout timeout,
		out T result,
		Func<T> action,
		bool throwIfTimeout = false)
	{
		if (action is null) throw new ArgumentNullException(nameof(action));
		using var readLock = new UpgradableReadLock(target, timeout, throwIfTimeout);
		result = readLock ? action() : default!;
		return readLock;
	}
}
