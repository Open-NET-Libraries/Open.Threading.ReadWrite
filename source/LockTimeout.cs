using System.Diagnostics.Contracts;

namespace Open.Threading;

// Note:
// Ultimately, ReaderWriterLockSlim uses milliseconds.
// TimeSpans are converted to milliseconds.

/// <summary>
/// Represents the valid millisecond timeout value for a lock.
/// </summary>
/// <remarks>
/// A millisecond value of -1  (System.Threading.Timeout.Infinite) indicates waiting indefinitely.
/// </remarks>
public readonly record struct LockTimeout
{
	private const string MustBeAtleastNeg1 = "Must be at least -1.";
	private const string MustBeLessThan32BitMax = "Must be less than maximum 32 bit integer (2147483647).";

	private readonly bool _isNotDefault;
	private readonly int _milliseconds;

	/// <summary>
	/// The number of milliseconds this timeout represents.
	/// </summary>
	public int Milliseconds => _isNotDefault ? _milliseconds : Timeout.Infinite;

	/// <summary>
	/// True if <see cref="Milliseconds"/> is not equal to <see cref="Timeout.Infinite"/>.
	/// </summary>
	public bool IsFinite => _isNotDefault && _milliseconds != Timeout.Infinite;

	/// <summary>
	/// True if <see cref="Milliseconds"/> is equal to <see cref="Timeout.Infinite"/>.
	/// </summary>
	public bool IsInfinite => !_isNotDefault || _milliseconds == Timeout.Infinite;

	/// <summary>
	/// Constructs a <see cref="LockTimeout"/> from an 32 bit integer.
	/// Default timeout is infinite.
	/// </summary>
	/// <exception cref="ArgumentOutOfRangeException">If the value is less than (-1).</exception>
	public LockTimeout(int milliseconds = Timeout.Infinite)
	{
		if (milliseconds < Timeout.Infinite)
			throw new ArgumentOutOfRangeException(nameof(milliseconds), milliseconds, MustBeAtleastNeg1);
		Contract.EndContractBlock();

		_isNotDefault = milliseconds != Timeout.Infinite;
		_milliseconds = _isNotDefault ? milliseconds : 0;
	}

	/// <summary>
	/// Constructs a <see cref="LockTimeout"/> from a <see cref="long"/>.
	/// </summary>
	/// <exception cref="ArgumentOutOfRangeException">If the value is less than (-1) or greater than <see cref="int.MaxValue"/>.</exception>
	public LockTimeout(long milliseconds)
	{
		if (milliseconds < Timeout.Infinite)
			throw new ArgumentOutOfRangeException(nameof(milliseconds), milliseconds, MustBeAtleastNeg1);
		if (milliseconds > int.MaxValue)
			throw new ArgumentOutOfRangeException(nameof(milliseconds), milliseconds, MustBeLessThan32BitMax);
		Contract.EndContractBlock();

		_isNotDefault = milliseconds != Timeout.Infinite;
		_milliseconds = _isNotDefault ? (int)milliseconds : 0;
	}

	/// <summary>
	/// Constructs a <see cref="LockTimeout"/> from a <see cref="double"/>.
	/// </summary>
	/// <inheritdoc cref="LockTimeout(long)"/>
	public LockTimeout(double milliseconds)
	{
		if (milliseconds < Timeout.Infinite)
			throw new ArgumentOutOfRangeException(nameof(milliseconds), milliseconds, MustBeAtleastNeg1);
		if (milliseconds > int.MaxValue)
			throw new ArgumentOutOfRangeException(nameof(milliseconds), milliseconds, MustBeLessThan32BitMax);
		Contract.EndContractBlock();

		_isNotDefault = milliseconds >= 0;
		_milliseconds = _isNotDefault ? (int)milliseconds : 0;
	}

	/// <summary>
	/// Constructs a <see cref="LockTimeout"/> from a <see cref="TimeSpan"/>.
	/// </summary>
	/// <exception cref="ArgumentOutOfRangeException">If the number of milliseconds value is less than (-1) or greater than <see cref="int.MaxValue"/>.</exception>
	public LockTimeout(TimeSpan timespan)
		: this(timespan.TotalMilliseconds) { }

	/// <summary>
	/// Implicitly converts a <see cref="LockTimeout"/> to its <see cref="Milliseconds"/> value.
	/// </summary>
	public static implicit operator int(LockTimeout timeout)
		=> timeout.Milliseconds;

	/// <inheritdoc cref="LockTimeout.LockTimeout(int)" />
	public static implicit operator LockTimeout(int milliseconds)
		=> new(milliseconds);

	/// <inheritdoc cref="LockTimeout.LockTimeout(long)" />
	public static implicit operator LockTimeout(long milliseconds)
		=> new(milliseconds);

	/// <inheritdoc cref="LockTimeout.LockTimeout(double)" />
	public static implicit operator LockTimeout(double milliseconds)
		=> new(milliseconds);

	/// <inheritdoc cref="LockTimeout.LockTimeout(TimeSpan)" />
	public static implicit operator LockTimeout(TimeSpan timespan)
		=> new(timespan);
}
