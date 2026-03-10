using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SAMe_VI.Service.Routing
{

    internal interface IFileHandler
    {
        internal abstract void Enqueue(string filePath);
        internal abstract Task ProcessAllAsync(CancellationToken ct = default);
    }

}
