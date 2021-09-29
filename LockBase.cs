/*!
 * @author electricessence / https://github.com/electricessence/
 * Based upon code from Stephen Cleary's Nitro library.
 */

using System;
using System.Threading;

namespace Open.Threading;

public abstract class LockBase<TSync> : IDisposable
	where TSync : class
{
	protected TSync? _target;
	public readonly bool LockHeld;

	protected LockBase(TSync target, bool lockHeld)
	{
		LockHeld = lockHeld;
		if (lockHeld)
			_target = target;
	}
	protected abstract void OnDispose(TSync? target);

	public void Dispose() => OnDispose(Interlocked.Exchange(ref _target, null));
}
