using VRage;
using ApiMemberAccessor = System.Func<object, int, object>;

namespace RichHudFramework
{
	using ControlMembers = MyTuple<
		ApiMemberAccessor,
		object
	>;

	namespace UI
	{
		public enum TerminalPageAccessors : int
		{
			Name = 1,

			Enabled = 2,
		}

		public interface ITerminalPage : IModRootMember
		{

			ControlMembers GetApiData();
		}
	}
}