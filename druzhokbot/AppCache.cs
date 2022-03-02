using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace druzhokbot
{
    internal static class AppCache
    {
        public static ConcurrentBag<long> UsersBanQueue = new ConcurrentBag<long>();
    }
}
