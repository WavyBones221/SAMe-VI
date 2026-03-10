using SAMe_VI.Object;
using System.Runtime.InteropServices;

namespace SAMe_VI.Service.Validators
{

    internal interface IValidator<in TDoc> 
    {
        Task<ValidationResult> ValidateAsync(TDoc doc, CancellationToken ct = default);
    }

}
