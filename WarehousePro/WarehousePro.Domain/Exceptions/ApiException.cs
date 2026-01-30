using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WarehousePro.Domain.Exceptions;

public class ApiException : Exception
{
    public ApiException(string message) : base(message) { }
    public ApiException(string message, params object[] args)
        : base(string.Format(System.Globalization.CultureInfo.CurrentCulture, message, args))
    {
    }
}