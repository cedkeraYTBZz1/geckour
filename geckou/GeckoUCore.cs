using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using geckou;

namespace FR_Injector
{
  public class GeckoUCore
  {
    public GeckoUConnect GUC;
    private static int default_port = 7331;
    private const uint packetsize = 1024;
    private const uint uplpacketsize = 1024;
    private uint maximumMemoryChunkSize = 1024;

    public GeckoUCore(string host) => this.GUC = new GeckoUConnect(host, GeckoUCore.default_port);

    public GeckoUCore(string host, int port) => this.GUC = new GeckoUConnect(host, port);

    private bool Connected { get; set; }

    public bool Connect()
    {
      if (this.Connected)
        this.Disconnect();
      this.Connected = false;
      try
      {
        this.GUC.Connect();
      }
      catch (IOException )
      {
        this.Disconnect();
        throw new GeckoUException(GeckoUException.ETCPErrorCode.noTCPGeckoFound);
      }
      Thread.Sleep(150);
      this.Connected = true;
      return true;
    }

    public bool Disconnect()
    {
      this.Connected = false;
      this.GUC.Close();
      return false;
    }

    protected GeckoUCore.FTDICommand GeckoURead(byte[] recbyte, uint nobytes)
    {
      uint bytesRead = 0;
      try
      {
        this.GUC.Read(recbyte, nobytes, ref bytesRead);
      }
      catch (IOException )
      {
        this.GUC.Close();
        return GeckoUCore.FTDICommand.CMD_FatalError;
      }
      return (int) bytesRead != (int) nobytes ? GeckoUCore.FTDICommand.CMD_ResultError : GeckoUCore.FTDICommand.CMD_OK;
    }

    protected GeckoUCore.FTDICommand GeckoUWrite(byte[] sendbyte, int nobytes)
    {
      uint bytesWritten = 0;
      try
      {
        this.GUC.Write(sendbyte, nobytes, ref bytesWritten);
      }
      catch (IOException )
      {
        this.GUC.Close();
        return GeckoUCore.FTDICommand.CMD_FatalError;
      }
      return (long) bytesWritten != (long) nobytes ? GeckoUCore.FTDICommand.CMD_ResultError : GeckoUCore.FTDICommand.CMD_OK;
    }

    public GeckoUCore.FTDICommand RawCommand(byte id) => this.GeckoUWrite(BitConverter.GetBytes((short) id), 1);

    private void SendCommand(GeckoUCommands.Command command)
    {
      uint bytesWritten = 0;
      this.GUC.Write(new byte[1]{ (byte) command }, 1, ref bytesWritten);
    }

    public byte[] ReadBytes(uint address, uint length)
    {
      try
      {
        this.RequestBytes(address, length);
        uint bytesRead1 = 0;
        byte[] buffer1 = new byte[1];
        this.GUC.Read(buffer1, 1U, ref bytesRead1);
        MemoryStream memoryStream = new MemoryStream();
        if (buffer1[0] == (byte) 176)
          return memoryStream.ToArray();
        uint length1;
        for (uint index = length; index > 0U; index -= length1)
        {
          length1 = index;
          if (length1 > this.maximumMemoryChunkSize)
            length1 = this.maximumMemoryChunkSize;
          byte[] buffer2 = new byte[(int) length1];
          uint bytesRead2 = 0;
          this.GUC.Read(buffer2, length1, ref bytesRead2);
          memoryStream.Write(buffer2, 0, (int) length1);
        }
        return memoryStream.ToArray();
      }
      catch (Exception )
      {
      }
      return (byte[]) null;
    }

    private void RequestBytes(uint address, uint length)
    {
      try
      {
        this.SendCommand(GeckoUCommands.Command.COMMAND_READ_MEMORY);
        uint bytesWritten = 0;
        byte[] bytes1 = BitConverter.GetBytes(ByteSwap.Swap(address));
        byte[] bytes2 = BitConverter.GetBytes(ByteSwap.Swap(address + length));
        this.GUC.Write(bytes1, 4, ref bytesWritten);
        this.GUC.Write(bytes2, 4, ref bytesWritten);
      }
      catch (Exception )
      {
      }
    }

    public uint UploadBytes(uint address, byte[] bytes)
    {
      int length = bytes.Length;
      uint num = address + (uint) bytes.Length;
      uint bytesWritten = 0;
      this.GUC.Write(bytes, length, ref bytesWritten);
      return num;
    }

    private void WritePartitionedBytes(uint address, IEnumerable<byte[]> byteChunks)
    {
      if (!(byteChunks is IList<byte[]> numArrayList))
        numArrayList = (IList<byte[]>) byteChunks.ToList<byte[]>();
      IEnumerable<byte[]> source = (IEnumerable<byte[]>) numArrayList;
      uint num1 = (uint) source.Sum<byte[]>((Func<byte[], int>) (chunk => chunk.Length));
      try
      {
        this.SendCommand(GeckoUCommands.Command.COMMAND_UPLOAD_MEMORY);
        uint bytesWritten = 0;
        byte[] bytes1 = BitConverter.GetBytes(ByteSwap.Swap(address));
        byte[] bytes2 = BitConverter.GetBytes(ByteSwap.Swap(address + num1));
        this.GUC.Write(bytes1, 4, ref bytesWritten);
        this.GUC.Write(bytes2, 4, ref bytesWritten);
        int num2 = (int) source.Aggregate<byte[], uint>(address, new Func<uint, byte[], uint>(this.UploadBytes));
      }
      catch (Exception ex)
      {
        Console.Write(ex.Message);
      }
    }

    private static IEnumerable<byte[]> Partition(byte[] bytes, uint chunkSize)
    {
      List<byte[]> numArrayList = new List<byte[]>();
      for (uint start = 0; (long) start < (long) bytes.Length; start += chunkSize)
      {
        long end = Math.Min((long) bytes.Length, (long) (start + chunkSize));
        numArrayList.Add(GeckoUCore.CopyOfRange(bytes, (long) start, end));
      }
      return (IEnumerable<byte[]>) numArrayList;
    }

    private static byte[] CopyOfRange(byte[] src, long start, long end)
    {
      long length = end - start;
      byte[] destinationArray = new byte[length];
      Array.Copy((Array) src, start, (Array) destinationArray, 0L, length);
      return destinationArray;
    }

    public void WriteBytes(uint address, byte[] bytes)
    {
      IEnumerable<byte[]> byteChunks = GeckoUCore.Partition(bytes, this.maximumMemoryChunkSize);
      this.WritePartitionedBytes(address, byteChunks);
    }

    public uint CallFunction(uint address, params uint[] args) => (uint) (this.CallFunction64(address, args) >> 32);

    public ulong CallFunction64(uint address, params uint[] args)
    {
      byte[] numArray = new byte[36];
      address = ByteSwap.Swap(address);
      BitConverter.GetBytes(address).CopyTo((Array) numArray, 0);
      for (int index = 0; index < 8; ++index)
      {
        if (index < args.Length)
          BitConverter.GetBytes(ByteSwap.Swap(args[index])).CopyTo((Array) numArray, 4 + index * 4);
        else
          BitConverter.GetBytes(4274704570U).CopyTo((Array) numArray, 4 + index * 4);
      }
      if (this.RawCommand((byte) 112) != GeckoUCore.FTDICommand.CMD_OK)
      {
        int num1 = (int) MessageBox.Show("your Wii U has been disconnected from ceeInject because a problem has occurred! check if the wiiu is turned off or if it has crashed!", "cedkeInject");
      }
      if (this.GeckoUWrite(numArray, numArray.Length) != GeckoUCore.FTDICommand.CMD_OK)
      {
        int num2 = (int) MessageBox.Show("your Wii U has been disconnected from ceeInject because a problem has occurred! check if the wiiu is turned off or if it has crashed!!", "cedkeInject");
      }
      if (this.GeckoURead(numArray, 8U) != GeckoUCore.FTDICommand.CMD_OK)
      {
        int num3 = (int) MessageBox.Show("your Wii U has been disconnected from ceeInject because a problem has occurred! check if the wiiu is turned off or if it has crashed!", "cedkeInject");
      }
      return ByteSwap.Swap(BitConverter.ToUInt64(numArray, 0));
    }

    public void WriteUIntToggle(uint address, uint value, uint originalValue, bool toggle)
    {
      if (toggle)
        this.WriteUInt(address, value);
      else
        this.WriteUInt(address, originalValue);
    }

    public void WriteLongToggle(uint address, long value, long originalValue, bool toggle)
    {
      if (toggle)
        this.WriteLong(address, value);
      else
        this.WriteLong(address, originalValue);
    }

    public void WriteULongToggle(uint address, ulong value, ulong originalValue, bool toggle)
    {
      if (toggle)
        this.WriteULong(address, value);
      else
        this.WriteULong(address, originalValue);
    }

    public void WriteUInt(uint address, uint value)
    {
      byte[] bytes = BitConverter.GetBytes(value);
      try
      {
        Array.Reverse((Array) bytes);
      }
      catch (Exception ex)
      {
        Console.Write(ex.Message);
      }
      this.WriteBytes(address, bytes);
    }

    public void WriteLong(uint address, long value)
    {
      byte[] bytes = BitConverter.GetBytes(value);
      try
      {
        Array.Reverse((Array) bytes);
      }
      catch (Exception ex)
      {
        Console.Write(ex.Message);
      }
      this.WriteBytes(address, bytes);
    }

    public void WriteULong(uint address, ulong value)
    {
      byte[] bytes = BitConverter.GetBytes(value);
      try
      {
        Array.Reverse((Array) bytes);
      }
      catch (Exception ex)
      {
        Console.Write(ex.Message);
      }
      this.WriteBytes(address, bytes);
    }

    public void WriteFloat(uint address, float value)
    {
      byte[] bytes = BitConverter.GetBytes(value);
      try
      {
        Array.Reverse((Array) bytes);
      }
      catch (ArgumentNullException ex)
      {
        Console.Write(ex.Message);
      }
      catch (RankException ex)
      {
        Console.Write(ex.Message);
      }
      this.WriteBytes(address, bytes);
    }

    public void WriteDouble(uint address, double value)
    {
      byte[] bytes = BitConverter.GetBytes(value);
      try
      {
        Array.Reverse((Array) bytes);
      }
      catch (ArgumentNullException ex)
      {
        Console.Write(ex.Message);
      }
      catch (RankException ex)
      {
        Console.Write(ex.Message);
      }
      this.WriteBytes(address, bytes);
    }

    public void WriteInt(uint address, int value)
    {
      byte[] bytes = BitConverter.GetBytes(value);
      try
      {
        Array.Reverse((Array) bytes);
      }
      catch (Exception ex)
      {
        Console.Write(ex.Message);
      }
      this.WriteBytes(address, bytes);
    }

    public void WriteShort(uint address, short value)
    {
      byte[] bytes = BitConverter.GetBytes(value);
      try
      {
        Array.Reverse((Array) bytes);
      }
      catch (Exception ex)
      {
        Console.Write(ex.Message);
      }
      this.WriteBytes(address, bytes);
    }

    public void WriteString(uint address, string value)
    {
      byte[] bytes = Encoding.ASCII.GetBytes(value);
      this.WriteBytes(address, bytes);
    }

    public void WriteStringUTF16(uint address, string value)
    {
      byte[] bytes = Encoding.BigEndianUnicode.GetBytes(value);
      this.WriteBytes(address, bytes);
    }

    public uint PeekUInt(uint address)
    {
      byte[] source = this.ReadBytes(address, 4U);
      uint num;
      try
      {
        Array.Reverse((Array) source);
        num = !((IEnumerable<byte>) source).Any<byte>() ? 0U : BitConverter.ToUInt32(source, 0);
      }
      catch (Exception )
      {
        return 0;
      }
      return num;
    }

    public long PeekLong(uint address)
    {
      byte[] source = this.ReadBytes(address, 8U);
      long num;
      try
      {
        Array.Reverse((Array) source);
        num = !((IEnumerable<byte>) source).Any<byte>() ? 0L : BitConverter.ToInt64(source, 0);
      }
      catch (Exception )
      {
        return 0;
      }
      return num;
    }

    public ulong PeekULong(uint address)
    {
      byte[] source = this.ReadBytes(address, 8U);
      ulong num;
      try
      {
        Array.Reverse((Array) source);
        num = !((IEnumerable<byte>) source).Any<byte>() ? 0UL : BitConverter.ToUInt64(source, 0);
      }
      catch (Exception )
      {
        return 0;
      }
      return num;
    }

    public float PeekFloat(uint address)
    {
      byte[] source = this.ReadBytes(address, 4U);
      float num;
      try
      {
        Array.Reverse((Array) source);
        num = !((IEnumerable<byte>) source).Any<byte>() ? 0.0f : BitConverter.ToSingle(source, 0);
      }
      catch (Exception )
      {
        return 0.0f;
      }
      return num;
    }

    public double PeekDouble(uint address)
    {
      byte[] source = this.ReadBytes(address, 4U);
      double num;
      try
      {
        Array.Reverse((Array) source);
        num = !((IEnumerable<byte>) source).Any<byte>() ? 0.0 : BitConverter.ToDouble(source, 0);
      }
      catch (Exception )
      {
        return 0.0;
      }
      return num;
    }

    public string PeekString(int length, uint address)
    {
      string empty = string.Empty;
      for (int index = 0; index < length; index += 4)
        empty += this.PeekUInt(address += (uint) index).ToString("X");
      return Encoding.UTF8.GetString(StringUtils.StringToByteArray(empty)).Replace("\0", "");
    }

    private uint ReadDataBufferSize()
    {
      this.SendCommand(GeckoUCommands.Command.COMMAND_GET_DATA_BUFFER_SIZE);
      uint bytesRead = 0;
      byte[] buffer = new byte[4];
      this.GUC.Read(buffer, 4U, ref bytesRead);
      Array.Reverse((Array) buffer);
      return BitConverter.ToUInt32(buffer, 0);
    }

    public uint ReadCodeHandlerAddress()
    {
      this.SendCommand(GeckoUCommands.Command.COMMAND_GET_CODE_HANDLER_ADDRESS);
      uint bytesRead = 0;
      byte[] buffer = new byte[4];
      this.GUC.Read(buffer, 4U, ref bytesRead);
      Array.Reverse((Array) buffer);
      return BitConverter.ToUInt32(buffer, 0);
    }

    public uint ReadAccountIdentifier()
    {
      this.SendCommand(GeckoUCommands.Command.COMMAND_ACCOUNT_IDENTIFIER);
      uint bytesRead = 0;
      byte[] buffer = new byte[4];
      this.GUC.Read(buffer, 4U, ref bytesRead);
      Array.Reverse((Array) buffer);
      return BitConverter.ToUInt32(buffer, 0);
    }

    public uint ReadVersionHash()
    {
      this.SendCommand(GeckoUCommands.Command.COMMAND_GET_VERSION_HASH);
      uint bytesRead = 0;
      byte[] buffer = new byte[4];
      this.GUC.Read(buffer, 4U, ref bytesRead);
      Array.Reverse((Array) buffer);
      return BitConverter.ToUInt32(buffer, 0);
    }

    public uint Mix(uint baseval, Decimal val)
    {
      Decimal num = 0M + val;
      return baseval + (uint) num;
    }

    public void ClearBuffer(byte[] buffer)
    {
      for (int index = 0; index < buffer.Length; ++index)
        buffer[index] = (byte) 0;
    }

    public void ClearString(uint address)
    {
      for (uint address1 = address; this.PeekUInt(address1) > 0U; address1 += 4U)
        this.WriteUInt(address1, 0U);
    }

    public void ClearString(uint addressStart, uint addressEnd)
    {
      for (uint address = addressStart; this.PeekUInt(address) > 0U && (int) address != (int) addressEnd; address += 4U)
        this.WriteUInt(address, 0U);
    }

    public bool GeckoUConnected()
    {
      try
      {
        this.WriteUInt(301990032U, 1U);
        return true;
      }
      catch
      {
        return false;
      }
    }

    public uint Mixmill(uint baseval, Decimal val)
    {
      Decimal num = 1000000M * val;
      return baseval + (uint) num;
    }

    public uint MixMillV2(uint baseval, Decimal val) => (uint) ((Decimal) baseval + 65536M * val);

    public uint MixMillX4V2(uint baseval, Decimal val) => (uint) ((Decimal) baseval + 262144M * val);

    public uint Mix4_V2(uint baseval, Decimal val) => (uint) ((Decimal) baseval + 4M * val);

    public uint Mixmillmoin(uint baseval, Decimal val)
    {
      Decimal num = 20000M * val;
      return baseval - (uint) num;
    }

    public uint Mixmillplus(uint baseval, Decimal val)
    {
      Decimal num = 20000M * val;
      return baseval + (uint) num;
    }

    public uint Mixmillplus7(uint baseval, Decimal val)
    {
      Decimal num = 70000M * val;
      return baseval + (uint) num;
    }

    public uint Mixmillmoin7(uint baseval, Decimal val)
    {
      Decimal num = 70000M * val;
      return baseval - (uint) num;
    }

    public uint Mixmill1moin(uint baseval, Decimal val)
    {
      Decimal num = 500000M * val;
      return baseval - (uint) num;
    }

    public uint Mixmill1plus(uint baseval, Decimal val)
    {
      Decimal num = 500000M * val;
      return baseval + (uint) num;
    }

    public uint Mixmill_2(uint baseval, Decimal val)
    {
      Decimal num = 10M * val;
      return baseval + (uint) num;
    }

    public uint adresseplus4(uint baseval, Decimal val)
    {
      Decimal num = 4M * val;
      return baseval + (uint) num;
    }

    public void clearString2(uint startAddress, uint endAddress)
    {
      uint num = endAddress - startAddress;
      for (int index = 0; (long) index < (long) num; index += 4)
        this.WriteUInt(startAddress + Convert.ToUInt32(index), 0U);
    }

    public void makeAssembly(uint adress, string input)
    {
      byte[] bytes = new byte[input.Length / 2];
      int index = 0;
      for (int startIndex = 0; startIndex < input.Length; startIndex += 2)
      {
        string str = input.Substring(startIndex, 2);
        bytes[index] = Convert.ToByte(str, 16);
        ++index;
      }
      this.WriteBytes(adress, bytes);
    }

    public enum FTDICommand
    {
      CMD_ResultError,
      CMD_FatalError,
      CMD_OK,
    }
  }
}
