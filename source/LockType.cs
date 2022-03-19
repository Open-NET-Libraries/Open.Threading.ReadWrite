namespace Open.Threading;

/// <summary>
/// An enumeration of possible lock types.
/// </summary>
public enum LockType : byte
{
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
	None,
	Read,
	UpgradableRead,
	Write,
	Monitor
#pragma warning restore CS1591 // Missing XML comment for publicly visible type or member
}
