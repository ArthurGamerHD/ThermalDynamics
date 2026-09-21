using Sandbox.ModAPI;
using System;

namespace RichHudFramework
{
	public static partial class Utils
	{
		public static class Xml
		{
/// <summary>TrySerialize operation.</summary>
			public static KnownException TrySerialize<T>(T obj, out string xmlOut)
			{
				KnownException exception = null;
				xmlOut = null;

				try
				{
					xmlOut = MyAPIGateway.Utilities.SerializeToXML(obj);
				}
				catch (Exception e)
				{
/// <summary>KnownException operation.</summary>
					exception = new KnownException("IO Error. Failed to generate XML.", e);
				}

				return exception;
			}

/// <summary>TryDeserialize operation.</summary>
			public static KnownException TryDeserialize<T>(string xmlIn, out T obj)
			{
				KnownException exception = null;
/// <summary>default operation.</summary>
				obj = default(T);

				try
				{
					obj = MyAPIGateway.Utilities.SerializeFromXML<T>(xmlIn);
				}
				catch (Exception e)
				{
/// <summary>KnownException operation.</summary>
					exception = new KnownException("IO Error. Unable to interpret XML.", e);
				}

				return exception;
			}
		}
	}
}