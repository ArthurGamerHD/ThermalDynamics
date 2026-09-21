using Sandbox.ModAPI;
using System;
using System.IO;

namespace RichHudFramework.IO
{
	public class LocalFileIO
	{
		public bool FileExists => MyAPIGateway.Utilities.FileExistsInLocalStorage(file, typeof(LocalFileIO));

		public readonly string file;

		private readonly object fileLock;

/// <summary>LocalFileIO operation.</summary>
		public LocalFileIO(string file)
		{
			this.file = file;
/// <summary>object operation.</summary>
			fileLock = new object();
		}

/// <summary>TryDuplicate operation.</summary>
		public KnownException TryDuplicate(string newName)
		{
			string data;
/// <summary>TryRead operation.</summary>
			KnownException exception = TryRead(out data);
			LocalFileIO newFile;

			if (exception == null && data != null)
			{
/// <summary>LocalFileIO operation.</summary>
				newFile = new LocalFileIO(newName);
				exception = newFile.TryWrite(data);
			}

			return exception;
		}

/// <summary>TryAppend operation.</summary>
		public KnownException TryAppend(string data)
		{
			string current;
/// <summary>TryRead operation.</summary>
			KnownException exception = TryRead(out current);

			if (exception == null && current != null)
			{
				current += data;
/// <summary>TryWrite operation.</summary>
				exception = TryWrite(current);
			}
			else
/// <summary>TryWrite operation.</summary>
				exception = TryWrite(data);

			return exception;
		}

/// <summary>TryRead operation.</summary>
		public KnownException TryRead(out byte[] stream)
		{
			KnownException exception = null;
			BinaryReader reader = null;

			lock (fileLock)
			{
				try
				{
					reader = MyAPIGateway.Utilities.ReadBinaryFileInLocalStorage(file, typeof(LocalFileIO));
					stream = reader.ReadBytes(reader.ReadInt32());
				}
				catch (Exception e)
				{
					stream = null;
/// <summary>KnownException operation.</summary>
					exception = new KnownException($"IO Error. Unable to read from {file}.", e);
				}
				finally
				{
					reader?.Close();
				}
			}

			return exception;
		}

/// <summary>TryRead operation.</summary>
		public KnownException TryRead(out string data)
		{
			KnownException exception = null;
			TextReader reader = null;
			data = null;

			lock (fileLock)
			{
				try
				{
					reader = MyAPIGateway.Utilities.ReadFileInLocalStorage(file, typeof(LocalFileIO));
					data = reader.ReadToEnd();
				}
				catch (Exception e)
				{
					data = null;
/// <summary>KnownException operation.</summary>
					exception = new KnownException($"IO Error. Unable to read from {file}.", e);
				}
				finally
				{
					reader?.Close();
				}
			}

			return exception;
		}

/// <summary>TryWrite operation.</summary>
		public KnownException TryWrite(byte[] stream)
		{
			KnownException exception = null;
			BinaryWriter writer = null;

			lock (fileLock)
			{
				try
				{
					writer = MyAPIGateway.Utilities.WriteBinaryFileInLocalStorage(file, typeof(LocalFileIO));
					writer.Write(stream.Length); // Write length prefix
					writer.Write(stream);
					writer.Flush();
				}
				catch (Exception e)
				{
/// <summary>KnownException operation.</summary>
					exception = new KnownException($"IO Error. Unable to write to {file}.", e);
				}
				finally
				{
					writer?.Close();
				}
			}

			return exception;
		}

/// <summary>TryWrite operation.</summary>
		public KnownException TryWrite(string data)
		{
			KnownException exception = null;
			TextWriter writer = null;

			lock (fileLock)
			{
				try
				{
					writer = MyAPIGateway.Utilities.WriteFileInLocalStorage(file, typeof(LocalFileIO));
					writer.Write(data);
					writer.Flush();
				}
				catch (Exception e)
				{
/// <summary>KnownException operation.</summary>
					exception = new KnownException($"IO Error. Unable to write to {file}.", e);
				}
				finally
				{
					writer?.Close();
				}
			}

			return exception;
		}
	}
}