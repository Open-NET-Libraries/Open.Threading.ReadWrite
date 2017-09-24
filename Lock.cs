using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace Open.Threading
{
    /// <summary>
    /// A simple lock class for allowing for a timeout.  If no timeout is desired, then simply use the lock(){} statement.
    /// 
    /// Example:
    /// <code>
    /// using(new Lock(sync,1000)) // If the timeout expires an exception is thrown.
    /// {
    ///     // do some synchronized work.
    /// }
    /// </code>
    /// ...or...
    /// <code>
    /// using(var syncLock = new Lock(sync,1000,false)) // If the timeout expires an exception is thrown.
    /// {
    ///     if(!syncLock.LockHeld)
    ///     {
    ///         // Do some unsynchronized work.
    ///     }
    ///     {
    ///         // do some synchronized work.
    ///     }
    /// }
    /// </code>
    /// </summary>
    public class Lock : LockBase<object>
    {
        public Lock(object target, int millisecondsTimeout, bool throwIfTimeout = true)
        : base(target, Monitor.TryEnter(target, millisecondsTimeout))
        {
            if (!LockHeld && throwIfTimeout)
                throw new TimeoutException(String.Format(
                    "Could not acquire a lock within the timeout specified. (millisecondsTimeout={0})", millisecondsTimeout));

        }

        protected override void OnDispose(object target)
        {
            if (target != null) Monitor.Exit(target);
        }
    }
}
