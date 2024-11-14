namespace Open.Threading;

/// <summary>
/// An enumeration of possible lock types.
/// </summary>
public enum LockType : byte
{
	/// <summary>
	/// No lock type.
	/// </summary>
	None,

	/// <summary>
	/// Read only lock.
	/// </summary>
	Read,

	/// <summary>
	/// Upgradable read lock.
	/// </summary>
	UpgradableRead,

	/// <summary>
	/// Write lock.
	/// </summary>
	Write,

	/// <summary>
	/// Monitor lock.
	/// </summary>
	Monitor
}
