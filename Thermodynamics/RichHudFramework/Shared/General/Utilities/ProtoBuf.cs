using Sandbox.ModAPI;
using System;

namespace RichHudFramework
{
	public static partial class Utils
	{
		public static class ProtoBuf
		{
/// <summary>TrySerialize operation.</summary>
			public static KnownException TrySerialize<T>(T obj, out byte[] dataOut)
			{
				KnownException exception = null;
				dataOut = null;
				
				try
				{
					dataOut = MyAPIGateway.Utilities.SerializeToBinary(obj);
				}
				catch (Exception e)
				{
/// <summary>KnownException operation.</summary>
					exception = new KnownException($"IO Error. Failed to generate binary from {typeof(T).Name}.", e);
				}

				return exception;
			}

/// <summary>TryDeserialize operation.</summary>
			public static KnownException TryDeserialize<T>(byte[] dataIn, out T obj)
			{
				KnownException exception = null;
/// <summary>default operation.</summary>
				obj = default(T);

				try
				{
					obj = MyAPIGateway.Utilities.SerializeFromBinary<T>(dataIn);
				}
				catch (Exception e)
				{
/// <summary>KnownException operation.</summary>
					exception = new KnownException($"IO Error. Failed to deserialize to {typeof(T).Name}.", e);
				}

				return exception;
			}
		}
	}
}