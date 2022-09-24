namespace Dnvm.Commands
{
	internal abstract class DnvmCommandBase
	{
		protected Program _dnvm;
		public DnvmCommandBase(Program dnvm)
		{
			_dnvm = dnvm;
		}
	}
}
