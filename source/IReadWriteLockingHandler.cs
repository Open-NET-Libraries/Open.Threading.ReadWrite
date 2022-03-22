using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;

namespace Open.Threading;

/// <summary>
/// A standardized interface for handling operations within a lock.
/// </summary>
public interface IReadWriteLockingHandler
{
	/// <summary>Acquires a lock of the requested <see cref="LockType"/> from the provider before invoking the <paramref name="action"/>.</summary>
	/// <inheritdoc cref="ReaderWriterLockSlimExensions.TryReadUpgradable(ReaderWriterLockSlim, LockTimeout, Action{ReaderWriterLockSlim}, bool)" />
	bool Try(LockType lockType, LockTimeout timeout, Action action, bool throwIfTimeout = false);

	/// <summary><inheritdoc cref="Try(LockType, LockTimeout, Action, bool)" path="/summary[1]"/></summary>
	/// <inheritdoc cref="ReaderWriterLockSlimExensions.TryReadUpgradable{T}(ReaderWriterLockSlim, LockTimeout, out T, Func{ReaderWriterLockSlim, T}, bool)" />
	bool Try<T>(LockType lockType, LockTimeout timeout, out T result, Func<T> action, bool throwIfTimeout = false);

	/// <inheritdoc cref="ReaderWriterLockSlimExensions.TryRead(ReaderWriterLockSlim, LockTimeout, Action, bool)"/>
	bool TryRead(LockTimeout timeout, Action action, bool throwIfTimeout = false);

	/// <inheritdoc cref="ReaderWriterLockSlimExensions.TryRead{T}(ReaderWriterLockSlim, LockTimeout, out T, Func{T}, bool)" />
	bool TryRead<T>(LockTimeout timeout, out T result, Func<T> action, bool throwIfTimeout = false);

	/// <inheritdoc cref="ReaderWriterLockSlimExensions.TryReadUpgradable(ReaderWriterLockSlim, LockTimeout, Action{ReaderWriterLockSlim}, bool)" />
	bool TryReadUpgradable(LockTimeout timeout, Action action, bool throwIfTimeout = false);

	/// <inheritdoc cref="ReaderWriterLockSlimExensions.TryReadUpgradable{T}(ReaderWriterLockSlim, LockTimeout, out T, Func{ReaderWriterLockSlim, T}, bool)" />
	bool TryReadUpgradable<T>(LockTimeout timeout, out T result, Func<T> action, bool throwIfTimeout = false);

	/// <inheritdoc cref="ReaderWriterLockSlimExensions.TryWrite(ReaderWriterLockSlim, LockTimeout, Action, bool)" />
	bool TryWrite(LockTimeout timeout, Action action, bool throwIfTimeout = false);

	/// <inheritdoc cref="ReaderWriterLockSlimExensions.TryWrite{T}(ReaderWriterLockSlim, LockTimeout, out T, Func{T}, bool)" />
	bool TryWrite<T>(LockTimeout timeout, out T result, Func<T> action, bool throwIfTimeout = false);
}

/// <inheritdoc />
public interface IReadWriteLockingHandler<TSync> : IReadWriteLockingHandler
{
	/// <summary>Acquires a lock of the requested <see cref="LockType"/> from the provider before invoking the <paramref name="action"/>.</summary>
	/// <inheritdoc cref="ReaderWriterLockSlimExensions.TryReadUpgradable(ReaderWriterLockSlim, LockTimeout, Action{ReaderWriterLockSlim}, bool)" />
	bool Try(LockType lockType, LockTimeout timeout, Action<TSync> action, bool throwIfTimeout = false);

	/// <summary><inheritdoc cref="Try(LockType, LockTimeout, Action{TSync}, bool)" path="/summary[1]"/></summary>
	/// <inheritdoc cref="ReaderWriterLockSlimExensions.TryReadUpgradable{T}(ReaderWriterLockSlim, LockTimeout, out T, Func{ReaderWriterLockSlim, T}, bool)" />
	bool Try<T>(LockType lockType, LockTimeout timeout, out T result, Func<TSync, T> action, bool throwIfTimeout = false);

	/// <inheritdoc cref="ReaderWriterLockSlimExensions.TryReadUpgradable(ReaderWriterLockSlim, LockTimeout, Action{ReaderWriterLockSlim}, bool)" />
	bool TryReadUpgradable(LockTimeout timeout, Action<TSync> action, bool throwIfTimeout = false);

	/// <inheritdoc cref="ReaderWriterLockSlimExensions.TryReadUpgradable{T}(ReaderWriterLockSlim, LockTimeout, out T, Func{ReaderWriterLockSlim, T}, bool)" />
	bool TryReadUpgradable<T>(LockTimeout timeout, out T result, Func<TSync, T> action, bool throwIfTimeout = false);
}

/// <summary>
/// Extensions for <see cref="IReadWriteLockingHandler{TSync}"/>.
/// </summary>
public static class ReadWriteLockingProviderExtensions
{
	[ExcludeFromCodeCoverage]
	private static void AssertOk(bool ok)
	{
		if (!ok)
		{
			const string message = $"{nameof(IReadWriteLockingHandler)} returned false when should have thrown.";
			throw new InvalidOperationException(message);
		}
	}

	[ExcludeFromCodeCoverage]
	private static void AssertOk<TSync>(bool ok)
	{
		if (!ok)
		{
			const string message = $"{nameof(IReadWriteLockingHandler<TSync>)} returned false when should have thrown.";
			throw new InvalidOperationException(message);
		}
	}

	/// <inheritdoc cref="ReaderWriterLockSlimExensions.Read(ReaderWriterLockSlim, LockTimeout, Action)"/>
	[ExcludeFromCodeCoverage]
	public static void Read(this IReadWriteLockingHandler provider, LockTimeout timeout, Action action)
	{
		if (provider is null) throw new ArgumentNullException(nameof(provider));
		AssertOk(provider.TryRead(timeout, action, true));
	}

	/// <inheritdoc cref="ReaderWriterLockSlimExensions.Read{T}(ReaderWriterLockSlim, LockTimeout, Func{T})" />
	[ExcludeFromCodeCoverage]
	public static T Read<T>(this IReadWriteLockingHandler provider, LockTimeout timeout, Func<T> action)
	{
		if (provider is null) throw new ArgumentNullException(nameof(provider));
		AssertOk(provider.TryRead(timeout, out var value, action, true));
		return value;
	}

	/// <inheritdoc cref="ReaderWriterLockSlimExensions.Read(ReaderWriterLockSlim, Action)" />
	[ExcludeFromCodeCoverage]
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static void Read(this IReadWriteLockingHandler provider, Action action)
		=> Read(provider, default, action);

	/// <inheritdoc cref="ReaderWriterLockSlimExensions.Read{T}(ReaderWriterLockSlim, Func{T})" />
	[ExcludeFromCodeCoverage]
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static T Read<T>(this IReadWriteLockingHandler provider, Func<T> action)
		=> Read(provider, default, action);

	/// <inheritdoc cref="ReaderWriterLockSlimExensions.ReadUpgradeable(ReaderWriterLockSlim, LockTimeout, Action{ReaderWriterLockSlim})" />
	[ExcludeFromCodeCoverage]
	public static void ReadUpgradable<TSync>(this IReadWriteLockingHandler<TSync> provider, LockTimeout timeout, Action<TSync> action)
	{
		if (provider is null) throw new ArgumentNullException(nameof(provider));
		AssertOk<TSync>(provider.TryReadUpgradable(timeout, action, true));
	}

	/// <inheritdoc cref="ReaderWriterLockSlimExensions.ReadUpgradeable{T}(ReaderWriterLockSlim, LockTimeout, Func{ReaderWriterLockSlim, T})" />
	[ExcludeFromCodeCoverage]
	public static T ReadUpgradable<TSync, T>(this IReadWriteLockingHandler<TSync> provider, LockTimeout timeout, Func<TSync, T> action)
	{
		if (provider is null) throw new ArgumentNullException(nameof(provider));
		AssertOk<TSync>(provider.TryReadUpgradable(timeout, out var value, action, true));
		return value;
	}

	/// <inheritdoc cref="ReaderWriterLockSlimExensions.ReadUpgradeable(ReaderWriterLockSlim, Action{ReaderWriterLockSlim})" />
	[ExcludeFromCodeCoverage]
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static void ReadUpgradable<TSync>(this IReadWriteLockingHandler<TSync> provider, Action<TSync> action)
		=> ReadUpgradable(provider, default, action);

	/// <inheritdoc cref="ReaderWriterLockSlimExensions.ReadUpgradeable{T}(ReaderWriterLockSlim, Func{ReaderWriterLockSlim, T})" />
	[ExcludeFromCodeCoverage]
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static T ReadUpgradable<TSync, T>(this IReadWriteLockingHandler<TSync> provider, Func<TSync, T> action)
		=> ReadUpgradable(provider, default, action);

	/// <inheritdoc cref="ReaderWriterLockSlimExensions.ReadUpgradeable(ReaderWriterLockSlim, LockTimeout, Action{ReaderWriterLockSlim})" />
	[ExcludeFromCodeCoverage]
	public static void ReadUpgradable(this IReadWriteLockingHandler provider, LockTimeout timeout, Action action)
	{
		if (provider is null) throw new ArgumentNullException(nameof(provider));
		AssertOk(provider.TryReadUpgradable(timeout, action, true));
	}

	/// <inheritdoc cref="ReaderWriterLockSlimExensions.ReadUpgradeable{T}(ReaderWriterLockSlim, LockTimeout, Func{ReaderWriterLockSlim, T})" />
	[ExcludeFromCodeCoverage]
	public static T ReadUpgradable<T>(this IReadWriteLockingHandler provider, LockTimeout timeout, Func<T> action)
	{
		if (provider is null) throw new ArgumentNullException(nameof(provider));
		AssertOk(provider.TryReadUpgradable(timeout, out var value, action, true));
		return value;
	}

	/// <inheritdoc cref="ReaderWriterLockSlimExensions.ReadUpgradeable(ReaderWriterLockSlim, Action{ReaderWriterLockSlim})" />
	[ExcludeFromCodeCoverage]
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static void ReadUpgradable(this IReadWriteLockingHandler provider, Action action)
		=> ReadUpgradable(provider, default, action);

	/// <inheritdoc cref="ReaderWriterLockSlimExensions.ReadUpgradeable{T}(ReaderWriterLockSlim, Func{ReaderWriterLockSlim, T})" />
	[ExcludeFromCodeCoverage]
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static T ReadUpgradable<T>(this IReadWriteLockingHandler provider, Func<T> action)
		=> ReadUpgradable(provider, default, action);

	/// <inheritdoc cref="ReaderWriterLockSlimExensions.Write(ReaderWriterLockSlim, LockTimeout, Action)" />
	[ExcludeFromCodeCoverage]
	public static void Write(this IReadWriteLockingHandler provider, LockTimeout timeout, Action action)
	{
		if (provider is null) throw new ArgumentNullException(nameof(provider));
		AssertOk(provider.TryWrite(timeout, action, true));
	}

	/// <inheritdoc cref="ReaderWriterLockSlimExensions.Write{T}(ReaderWriterLockSlim, LockTimeout, Func{T})" />
	[ExcludeFromCodeCoverage]
	public static T Write<T>(this IReadWriteLockingHandler provider, LockTimeout timeout, Func<T> action)
	{
		if (provider is null) throw new ArgumentNullException(nameof(provider));
		AssertOk(provider.TryWrite(timeout, out var value, action, true));
		return value;
	}

	/// <inheritdoc cref="ReaderWriterLockSlimExensions.Write(ReaderWriterLockSlim, Action)" />
	[ExcludeFromCodeCoverage]
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static void Write(this IReadWriteLockingHandler provider, Action action)
		=> Write(provider, default, action);

	/// <inheritdoc cref="ReaderWriterLockSlimExensions.Write{T}(ReaderWriterLockSlim, Func{T})" />
	[ExcludeFromCodeCoverage]
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static T Write<T>(this IReadWriteLockingHandler provider, Func<T> action)
		=> Write(provider, default, action);

	/// <inheritdoc cref="ReaderWriterLockSlimExensions.TryWriteConditional(ReaderWriterLockSlim, LockTimeout, Func{bool}, Action, bool)"/>
	public static bool TryWriteConditional(
		this IReadWriteLockingHandler provider,
		LockTimeout timeout,
		Func<bool> condition,
		Action action,
		bool throwIfTimeout = false)
	{
		if (condition is null) throw new ArgumentNullException(nameof(condition));
		if (action is null) throw new ArgumentNullException(nameof(action));
		Contract.EndContractBlock();

		return provider.TryReadUpgradable(timeout, out bool ok, condition, throwIfTimeout)
			&& ok && provider.TryWrite(timeout, action, throwIfTimeout);
	}

	/// <inheritdoc cref="ReaderWriterLockSlimExensions.TryWriteConditional{T}(ReaderWriterLockSlim, LockTimeout, ref T, Func{bool}, Func{T}, bool)"/>
	public static bool TryWriteConditional<T>(
		this IReadWriteLockingHandler provider,
		LockTimeout timeout,
		ref T result,
		Func<bool> condition,
		Func<T> action,
		bool throwIfTimeout = false)
	{
		if (condition is null) throw new ArgumentNullException(nameof(condition));
		if (action is null) throw new ArgumentNullException(nameof(action));
		Contract.EndContractBlock();

		return provider.TryReadUpgradable(timeout, out bool ok, condition, throwIfTimeout)
			&& ok && provider.TryWrite(timeout, out result, action, throwIfTimeout);
	}

	/// <inheritdoc cref="ReaderWriterLockSlimExensions.TryWriteConditional(ReaderWriterLockSlim, LockTimeout, Func{bool, bool}, Action, bool)"/>
	public static bool TryWriteConditional(
		this IReadWriteLockingHandler provider,
		LockTimeout timeout,
		Func<bool, bool> condition,
		Action action,
		bool throwIfTimeout = false)
	{
		if (condition is null) throw new ArgumentNullException(nameof(condition));
		if (action is null) throw new ArgumentNullException(nameof(action));
		Contract.EndContractBlock();

		return condition(false)
			&& TryWriteConditional(provider,
				timeout, () => condition(true), action,
				throwIfTimeout);
	}

	/// <inheritdoc cref="ReaderWriterLockSlimExensions.TryWriteConditional{T}(ReaderWriterLockSlim, LockTimeout, ref T, Func{bool, bool}, Func{T}, bool)"/>
	public static bool TryWriteConditional<T>(
		this IReadWriteLockingHandler provider,
		LockTimeout timeout,
		ref T result,
		Func<bool, bool> condition,
		Func<T> action,
		bool throwIfTimeout = false)
	{
		if (condition is null) throw new ArgumentNullException(nameof(condition));
		if (action is null) throw new ArgumentNullException(nameof(action));
		Contract.EndContractBlock();

		bool ConditionUpgraded() => condition(true);

		return condition(false)
			&& TryWriteConditional(provider,
				timeout, ref result,
				ConditionUpgraded, action,
				throwIfTimeout);
	}

	/// <inheritdoc cref="ReaderWriterLockSlimExensions.WriteConditional(ReaderWriterLockSlim, LockTimeout, Func{bool}, Action)"/>
	[ExcludeFromCodeCoverage]
	public static bool WriteConditional(
		this IReadWriteLockingHandler provider,
		LockTimeout timeout,
		Func<bool> condition,
		Action action)
		=> provider.TryWriteConditional(timeout, condition, action, true);

	/// <inheritdoc cref="ReaderWriterLockSlimExensions.WriteConditional{T}(ReaderWriterLockSlim, LockTimeout, ref T, Func{bool}, Func{T})"/>
	[ExcludeFromCodeCoverage]
	public static bool WriteConditional<T>(
		this IReadWriteLockingHandler provider,
		LockTimeout timeout,
		ref T result,
		Func<bool> condition,
		Func<T> action)
		=> provider.TryWriteConditional(timeout, ref result, condition, action, true);

	/// <inheritdoc cref="ReaderWriterLockSlimExensions.WriteConditional(ReaderWriterLockSlim, LockTimeout, Func{bool, bool}, Action)"/>
	[ExcludeFromCodeCoverage]
	public static bool WriteConditional(
		this IReadWriteLockingHandler provider,
		LockTimeout timeout,
		Func<bool, bool> condition,
		Action action)
		=> provider.TryWriteConditional(timeout, condition, action, true);

	/// <inheritdoc cref="ReaderWriterLockSlimExensions.WriteConditional{T}(ReaderWriterLockSlim, LockTimeout, ref T, Func{bool, bool}, Func{T})"/>
	[ExcludeFromCodeCoverage]
	public static bool WriteConditional<T>(
		this IReadWriteLockingHandler provider,
		LockTimeout timeout,
		ref T result,
		Func<bool, bool> condition,
		Func<T> action)
		=> provider.TryWriteConditional(timeout, ref result, condition, action, true);

	/// <inheritdoc cref="ReaderWriterLockSlimExensions.WriteConditional(ReaderWriterLockSlim, LockTimeout, Func{bool}, Action)"/>
	[ExcludeFromCodeCoverage]
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool WriteConditional(
		this IReadWriteLockingHandler provider,
		Func<bool> condition,
		Action action)
		=> WriteConditional(provider, default, condition, action);

	/// <inheritdoc cref="ReaderWriterLockSlimExensions.WriteConditional{T}(ReaderWriterLockSlim, LockTimeout, ref T, Func{bool}, Func{T})"/>
	[ExcludeFromCodeCoverage]
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool WriteConditional<T>(
		this IReadWriteLockingHandler provider,
		ref T result,
		Func<bool> condition,
		Func<T> action)
		=> WriteConditional(provider, default, ref result, condition, action);

	/// <inheritdoc cref="ReaderWriterLockSlimExensions.WriteConditional(ReaderWriterLockSlim, LockTimeout, Func{bool, bool}, Action)"/>
	[ExcludeFromCodeCoverage]
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool WriteConditional(
		this IReadWriteLockingHandler provider,
		Func<bool, bool> condition,
		Action action)
		=> WriteConditional(provider, default, condition, action);

	/// <inheritdoc cref="ReaderWriterLockSlimExensions.WriteConditional{T}(ReaderWriterLockSlim, LockTimeout, ref T, Func{bool, bool}, Func{T})"/>
	[ExcludeFromCodeCoverage]
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool WriteConditional<T>(
		this IReadWriteLockingHandler provider,
		ref T result,
		Func<bool, bool> condition,
		Func<T> action)
		=> WriteConditional(provider, default, ref result, condition, action);

	/// <inheritdoc cref="ReaderWriterLockSlimExensions.ReadWriteConditional(ReaderWriterLockSlim, Func{bool, bool}, Action)"/>
	[ExcludeFromCodeCoverage]
	public static bool ReadWriteConditional(
		this IReadWriteLockingHandler provider,
		Func<bool, bool> condition,
		Action action)
		=> TryReadWriteConditional(provider, default, condition, action, true);

	/// <inheritdoc cref="ReaderWriterLockSlimExensions.ReadWriteConditional(ReaderWriterLockSlim, LockTimeout, Func{bool, bool}, Action)"/>
	[ExcludeFromCodeCoverage]
	public static bool ReadWriteConditional(
		this IReadWriteLockingHandler provider,
		LockTimeout timeout,
		Func<bool, bool> condition,
		Action action)
		=> TryReadWriteConditional(provider, timeout, condition, action, true);

	/// <inheritdoc cref="ReaderWriterLockSlimExensions.ReadWriteConditional{T}(ReaderWriterLockSlim, LockTimeout, ref T, Func{bool, bool}, Func{T})"/>
	[ExcludeFromCodeCoverage]
	public static bool ReadWriteConditional<T>(
		this IReadWriteLockingHandler provider,
		ref T result,
		Func<bool, bool> condition,
		Func<T> action)
		=> ReadWriteConditional(provider, default, ref result, condition, action);

	/// <inheritdoc cref="ReaderWriterLockSlimExensions.ReadWriteConditional{T}(ReaderWriterLockSlim, ref T, Func{bool, bool}, Func{T})" />
	[ExcludeFromCodeCoverage]
	public static bool ReadWriteConditional<T>(
		this IReadWriteLockingHandler provider,
		LockTimeout timeout,
		ref T result,
		Func<bool, bool> condition,
		Func<T> action)
		=> TryReadWriteConditional(provider, timeout, ref result, condition, action, true);

	/// <inheritdoc cref="ReaderWriterLockSlimExensions.TryReadWriteConditional(ReaderWriterLockSlim, LockTimeout, Func{bool, bool}, Action, bool)"/>
	public static bool TryReadWriteConditional(
		this IReadWriteLockingHandler provider,
		LockTimeout timeout,
		Func<bool, bool> condition,
		Action action,
		bool throwIfTimeout = false)
	{
		if (condition is null) throw new ArgumentNullException(nameof(condition));
		if (action is null) throw new ArgumentNullException(nameof(action));
		Contract.EndContractBlock();

		return provider.TryRead(timeout, out var ok, () => condition(false), throwIfTimeout) && ok
			&& TryWriteConditional(provider, timeout, () => condition(true), action, throwIfTimeout);
	}

	/// <inheritdoc cref="ReaderWriterLockSlimExensions.TryReadWriteConditional{T}(ReaderWriterLockSlim, LockTimeout, ref T, Func{bool, bool}, Func{T}, bool)"/>
	public static bool TryReadWriteConditional<T>(
		this IReadWriteLockingHandler provider,
		LockTimeout timeout,
		ref T result,
		Func<bool, bool> condition,
		Func<T> action,
		bool throwIfTimeout = false)
	{
		if (condition is null) throw new ArgumentNullException(nameof(condition));
		if (action is null) throw new ArgumentNullException(nameof(action));
		Contract.EndContractBlock();

		return provider.TryRead(timeout, out var ok, () => condition(false), throwIfTimeout) && ok
			&& TryWriteConditional(provider, timeout, ref result, () => condition(true), action, throwIfTimeout);
	}

	/// <summary>
	/// If <paramref name="getValue"/> returns null, the value is acquired from <paramref name="createValue"/>.
	/// </summary>
	/// <typeparam name="T">The return type.</typeparam>
	/// <param name="provider">The <see cref="IReadWriteLockingHandler"/> to acquire a lock from.</param>
	/// <param name="getValue">The function to get the value.</param>
	/// <param name="createValue">The create value factory.</param>
	/// <returns>The value acquired.</returns>
	[ExcludeFromCodeCoverage]
	public static T GetOrCreateValue<T>(
		this IReadWriteLockingHandler provider,
		Func<T?> getValue,
		Func<T> createValue)
		=> GetOrCreateValue(provider, default, getValue, createValue);

	/// <summary>
	/// If <paramref name="getValue"/> returns null, the value is acquired from <paramref name="createValue"/>.
	/// </summary>
	/// <typeparam name="T">The return type.</typeparam>
	/// <param name="provider">The <see cref="IReadWriteLockingHandler"/> to acquire a lock from.</param>
	/// <param name="timeout"><inheritdoc cref="Lock(object, LockTimeout, bool)" path="/param[@name='timeout']"/></param>
	/// <param name="getValue">The function to get the value.</param>
	/// <param name="createValue">The create value factory.</param>
	/// <returns>The value acquired.</returns>
	/// <exception cref="TimeoutException"><inheritdoc cref="Write(IReadWriteLockingHandler, LockTimeout, Action)" path="/exception"/></exception>
	public static T GetOrCreateValue<T>(
		this IReadWriteLockingHandler provider,
		LockTimeout timeout,
		Func<T?> getValue,
		Func<T> createValue)
	{
		if (getValue is null) throw new ArgumentNullException(nameof(getValue));
		if (createValue is null) throw new ArgumentNullException(nameof(createValue));
		Contract.EndContractBlock();

		T? result = default;
		ReadWriteConditional<T?>(provider,
			timeout,
			ref result,
			_ => (result = getValue()) is null,
			createValue);

		return result!;
	}
}
