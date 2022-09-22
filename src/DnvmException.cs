using System;

namespace Dnvm;
internal class DnvmException : ApplicationException
{
	public DnvmException(string? message = null) : base(message) { }
}
