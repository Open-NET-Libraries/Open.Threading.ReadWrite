using System;
using System.Collections.Generic;
using System.Text;

namespace Open.Threading
{
    public enum LockType : byte
    {
        Read,
        ReadUpgradeable,
        Write
    }
}
