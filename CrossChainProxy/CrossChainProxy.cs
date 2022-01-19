using System;
using System.ComponentModel;
using System.Numerics;
using Neo;
using Neo.SmartContract;
using Neo.SmartContract.Framework;
using Neo.SmartContract.Framework.Attributes;
using Neo.SmartContract.Framework.Native;
using Neo.SmartContract.Framework.Services;

namespace CrossChainProxy
{
    [DisplayName("CrossChainProxy")]
    [ManifestExtra("Author", "Switcheo Labs")]
    [ManifestExtra("Email", "engineering@switcheo.network")]
    [ManifestExtra("Description", "This is the CrossChainProxy for Polynetwork to Carbon")]
    public class CrossChainProxy : SmartContract
    {

        [InitialValue("44baf1fac6dc465d6318e84911fd9bf536c5d6fd", ContractParameterType.ByteArray)] // little endian
        private static readonly byte[] CCMCScriptHash = default;

        [InitialValue("NYxb4fSZVKAz8YsgaPK2WkT3KcAE9b3Vag", ContractParameterType.Hash160)]
        private static readonly UInt160 owner = default;
        private static readonly byte[] ownerKey = new byte[] { 0x01, 0x01 };
        private static readonly byte[] operatorKey = new byte[] { 0x01, 0x02 };
        private static readonly byte[] pauseKey = new byte[] { 0x01, 0x03 };
        private static readonly byte[] proxyAddressPrefix = new byte[] { 0x01, 0x04 };
        private static readonly byte[] assetAddressPrefix = new byte[] { 0x01, 0x05 };
        private static readonly BigInteger thisChainId = 44;
        private static readonly BigInteger targetChainId = 5; // TestNet: 198

        // Events
        public static event Action<UInt160> TransferOwnershipEvent;
        public static event Action<UInt160, BigInteger, byte[], byte[]> RegisterAssetEvent;
        public static event Action<UInt160, UInt160, BigInteger, byte[], byte[], BigInteger> LockEvent;
        public static event Action<UInt160, UInt160, BigInteger> UnlockEvent;
        public static event Action<byte[], UInt160> DeployEvent;
        public static event Action<object> notify;

        public static void _deploy(object data, bool update)
        {
            Storage.Put(Storage.CurrentContext, ownerKey, owner);

            DeployEvent(ownerKey, owner);
        }

        /*
        * Owner
        */

        public static bool IsOwner()
        {
            UInt160 owner = (UInt160)Storage.Get(Storage.CurrentContext, ownerKey);
            return Runtime.CheckWitness(owner);
        }

        public static bool TransferOwnership(UInt160 newOwner)
        {
            Assert(newOwner.Length == 20, "transferOwnership: newOwner.Length != 20");
            Assert(IsOwner(), "transferOwnership: not owner");
            Storage.Put(Storage.CurrentContext, ownerKey, newOwner);

            TransferOwnershipEvent(newOwner);
            return true;
        }

        public static bool SetOperator(UInt160 operatorAddress)
        {
            Assert(IsOwner(), "setOperator: not owner");
            Storage.Put(Storage.CurrentContext, operatorKey.Concat(operatorAddress), 1);
            return true;
        }

        public static bool RemoveOperator(UInt160 operatorAddress)
        {
            Assert(IsOwner(), "removeOperator: not owner");
            Storage.Delete(Storage.CurrentContext, operatorKey.Concat(operatorAddress));
            return true;
        }

        // used to upgrade this proxy contract
        public static bool Update(ByteString nefFile, string manifest)
        {
            Assert(IsOwner(), "update: not owner");
            ContractManagement.Update(nefFile, manifest);
            return true;
        }

        /*
        * Operator
        */

        public static bool IsOperator(UInt160 operatorAddress)
        {
            ByteString result = Storage.Get(Storage.CurrentContext, operatorKey.Concat(operatorAddress));
            if (result is null)
            {
                return false;
            }
            else
            {
                return Runtime.CheckWitness(operatorAddress);
            }
        }

        public static bool Pause(UInt160 operatorAddress)
        {
            Assert(IsOperator(operatorAddress), "pause: not owner");
            Storage.Put(Storage.CurrentContext, pauseKey, new byte[] { 0x01 });
            return true;
        }

        public static bool Unpause(UInt160 operatorAddress)
        {
            Assert(IsOperator(operatorAddress), "unpause: not owner");
            Storage.Delete(Storage.CurrentContext, pauseKey);
            return true;
        }

        public static bool IsPaused()
        {
            return Storage.Get(Storage.CurrentContext, pauseKey).Equals(new byte[] { 0x01 });
        }

        /*
        * Cross-chain
        */

        public static bool RegisterAsset(byte[] inputBytes, byte[] targetProxyAddress, BigInteger chainId)
        {
            Assert(!IsPaused(), "lock: proxy is paused");
            Assert(Runtime.CallingScriptHash == (UInt160)CCMCScriptHash, "registerAsset: only allowed to be called by CCMC");
            Assert(chainId == targetChainId, "registerAsset: wrong chain id");

            object[] results = DeserializeRegisterAssetArgs(inputBytes);
            var targetAssetAddress = (byte[])results[0];
            var thisAssetAddress = (UInt160)results[1];

            Assert(targetAssetAddress.Length > 0, "registerAsset: target asset address is empty");
            Assert(thisAssetAddress.Length == 20, "registerAsset: thisAssetAddress must be 20-byte long");
            // Make sure fromAssetAddress has balanceOf method
            BigInteger balance = GetAssetBalance(thisAssetAddress);

            // Make sure not already registered
            Assert(GetAssetAddress(thisAssetAddress).Length == 0, "registerAsset: asset address already registered");
            Assert(GetProxyAddress(thisAssetAddress).Length == 0, "registerAsset: proxy address already registered");

            // add mapping for this asset => target address
            Storage.Put(Storage.CurrentContext, assetAddressPrefix.Concat(thisAssetAddress), targetAssetAddress);

            // add mapping for this asset => target proxy
            Storage.Put(Storage.CurrentContext, proxyAddressPrefix.Concat(thisAssetAddress), targetProxyAddress);

            RegisterAssetEvent(thisAssetAddress, targetChainId, targetProxyAddress, targetAssetAddress);
            return true;
        }

        // used to lock asset into proxy contract
        public static bool Lock(UInt160 fromAssetAddress, UInt160 fromAddress, byte[] toAddress, BigInteger amount, BigInteger feeAmount, byte[] feeAddress, BigInteger nonce)
        {
            Assert(!IsPaused(), "lock: proxy is paused");
            Assert(fromAssetAddress.Length == 20, "lock: fromAssetAddress must be 20-byte long");
            Assert(fromAddress.Length == 20, "lock: fromAddress must be 20-byte long");
            Assert(toAddress.Length == 0, "lock: toAddress cannot be empty");
            Assert(amount > 0, "lock: amount must be greater than 0");
            Assert(feeAmount >= 0, "lock: feeAmount must be positive");
            Assert(feeAmount == 0 || feeAddress.Length > 0, "lock: feeAdress cannot be empty if feeAmount is not zero");
            Assert(!fromAddress.Equals(Runtime.ExecutingScriptHash), "lock: can not lock self");

            // get the proxy contract on target chain
            var toProxyAddress = GetProxyAddress(fromAssetAddress);

            // get the corresbonding asset on to chain
            var toAssetAddress = GetAssetAddress(fromAssetAddress);

            // transfer asset from fromAddress to proxy contract address, use dynamic call to call nep5 token's contract "transfer"
            bool success = (bool)Contract.Call((UInt160)fromAssetAddress, "transfer", CallFlags.All, new object[] { fromAddress, Runtime.ExecutingScriptHash, amount, null });
            Assert(success, "lock: failed to transfer NEP5 token to CrossChainProxy");

            // construct args for proxy contract on target chain
            var inputArgs = SerializeLockArgs((byte[])fromAssetAddress, toAssetAddress, toAddress, amount, feeAmount, feeAddress, (byte[])fromAddress, nonce);

            // dynamic call CCMC
            success = (bool)Contract.Call((UInt160)CCMCScriptHash, "crossChain", CallFlags.All, new object[] { targetChainId, toProxyAddress, "unlock", inputArgs, Runtime.ExecutingScriptHash });
            Assert(success, "lock: failed to call CCMC");

            LockEvent(fromAssetAddress, fromAddress, targetChainId, toAssetAddress, toAddress, amount);
            return true;
        }

        // Methods of actual execution, used to unlock asset from proxy contract
        public static bool Unlock(byte[] inputBytes, byte[] fromProxyContract, BigInteger chainId)
        {
            Assert(!IsPaused(), "lock: proxy is paused");
            Assert(Runtime.CallingScriptHash == (UInt160)CCMCScriptHash, "unlock: only allowed to be called by CCMC");
            Assert(chainId == targetChainId, "registerAsset: wrong chain id");
            Assert(fromProxyContract.Length > 0, "unlock: fromProxyContract cannot be empty");

            // parse the args bytes constructed in source chain proxy contract, passed by multi-chain
            object[] results = DeserializeUnlockArgs(inputBytes);
            var fromAssetAddress = (ByteString)results[0];
            var toAssetAddress = (UInt160)results[1];
            var toAddress = (UInt160)results[2];
            var amount = (BigInteger)results[3];
            Assert(toAssetAddress.Length == 20, "unlock: toAssetAddress hash must be 20-byte long");
            Assert(toAddress.Length == 20, "unlock: toAddress must be 20-byte long");
            Assert(amount > 0, "unlock: amount must be greater than 0");

            var proxyAddress = GetProxyAddress(toAssetAddress);
            Assert((UInt160)fromProxyContract == proxyAddress, "unlock: fromProxyContract is invalid");

            var assetAddress = GetAssetAddress(toAssetAddress);
            Assert(fromAssetAddress == (ByteString)assetAddress, "unlock: toAssetAddress is invalid");

            // transfer asset from proxy contract to toAddress
            bool success = (bool)Contract.Call((UInt160)toAssetAddress, "transfer", CallFlags.All, new object[] { Runtime.ExecutingScriptHash, toAddress, amount, null });
            Assert(success, "unlock: failed to transfer NEP5 token From CrossChainProxy to toAddress");

            UnlockEvent(toAssetAddress, toAddress, amount);
            return true;
        }

        /*
        * Getters
        */

        public static BigInteger GetAssetBalance(UInt160 assetAddress)
        {
            BigInteger balance = (BigInteger)Contract.Call(assetAddress, "balanceOf", CallFlags.All, new object[] { Runtime.ExecutingScriptHash });
            return balance;
        }


        // get target proxy contract address according local asset hash
        public static UInt160 GetProxyAddress(UInt160 thisAssetAddress)
        {
            return (UInt160)Storage.Get(Storage.CurrentContext, proxyAddressPrefix.Concat(thisAssetAddress));
        }

        // get target asset contract address according to local asset hash
        public static byte[] GetAssetAddress(UInt160 thisAssetAddress)
        {
            return (byte[])Storage.Get(Storage.CurrentContext, assetAddressPrefix.Concat(thisAssetAddress));
        }

        /*
        * Serialization
        */

        private static object[] DeserializeUnlockArgs(byte[] buffer)
        {
            var offset = 0;
            ByteString fromAssetAddress;
            ByteString toAssetAddress;
            ByteString toAddress;
            BigInteger amount;
            (fromAssetAddress, offset) = ReadVarBytes(buffer, offset);
            (toAssetAddress, offset) = ReadVarBytes(buffer, offset);
            (toAddress, offset) = ReadVarBytes(buffer, offset);
            (amount, offset) = ReadUint255(buffer, offset);
            return new object[] { fromAssetAddress, toAssetAddress, toAddress, amount };
        }

        private static object[] DeserializeRegisterAssetArgs(byte[] buffer)
        {
            var offset = 0;
            ByteString targetAssetAddress;
            ByteString thisAssetAddress;
            (targetAssetAddress, offset) = ReadVarBytes(buffer, offset);
            (thisAssetAddress, offset) = ReadVarBytes(buffer, offset);
            return new object[] { targetAssetAddress, thisAssetAddress };
        }

        private static (BigInteger, int) ReadUint255(byte[] buffer, int offset)
        {
            Assert(offset + 32 <= buffer.Length, "readUint255: buffer length insufficient");
            BigInteger result = new BigInteger(buffer.Range(offset, 32));
            offset = offset + 32;
            Assert(result >= 0, "readUint255: result should > 0"); //uint255 exceed max size, can not concat 0x00
            return (result, offset);
        }

        // return [BigInteger: value, int: offset]
        private static (BigInteger, int) ReadVarInt(byte[] buffer, int offset)
        {
            (ByteString fb, int newOffset) = ReadBytes(buffer, offset, 1); // read the first byte
            if (fb.Equals((ByteString)new byte[] { 0xfd }))
            {
                return (new BigInteger(buffer.Range(newOffset, 2).Concat(new byte[] { 0x00 })), newOffset + 2);
            }
            else if (fb.Equals((ByteString)new byte[] { 0xfe }))
            {
                return (new BigInteger(buffer.Range(newOffset, 4).Concat(new byte[] { 0x00 })), newOffset + 4);
            }
            else if (fb.Equals((ByteString)new byte[] { 0xff }))
            {
                return (new BigInteger(buffer.Range(newOffset, 8).Concat(new byte[] { 0x00 })), newOffset + 8);
            }
            else
            {
                return (new BigInteger(((byte[])fb).Concat(new byte[] { 0x00 })), newOffset);
            }
        }

        // return [byte[], new offset]
        private static (ByteString, int) ReadVarBytes(byte[] buffer, int offset)
        {
            BigInteger count;
            int newOffset;
            (count, newOffset) = ReadVarInt(buffer, offset);
            return ReadBytes(buffer, newOffset, (int)count);
        }

        // return [byte[], new offset]
        private static (ByteString, int) ReadBytes(byte[] buffer, int offset, int count)
        {
            Assert(offset + count <= buffer.Length, "readBytes: buffer length insufficient");
            return ((ByteString)buffer.Range(offset, count), offset + count);
        }

        private static byte[] SerializeLockArgs(byte[] fromAssetAddress, byte[] toAssetAddress, byte[] toAddress,
          BigInteger amount, BigInteger feeAmount, byte[] feeAddress, byte[] fromAddress, BigInteger nonce)
        {
            var buffer = new byte[] { };
            buffer = WriteVarBytes(fromAssetAddress, buffer);
            buffer = WriteVarBytes(toAssetAddress, buffer);
            buffer = WriteVarBytes(toAddress, buffer);
            buffer = WriteUint255(amount, buffer);
            buffer = WriteUint255(feeAmount, buffer);
            buffer = WriteVarBytes(feeAddress, buffer);
            buffer = WriteVarBytes(fromAddress, buffer);
            buffer = WriteUint255(nonce, buffer);
            return buffer;
        }

        private static byte[] WriteUint255(BigInteger value, byte[] source)
        {
            Assert(value >= 0, "writeUint255: value out of range of uint255");
            var v = padRight(value.ToByteArray(), 32);
            return source.Concat(v); // no need to concat length, fix 32 bytes
        }

        private static byte[] writeVarInt(BigInteger value, byte[] source)
        {
            if (value < 0)
            {
                notify("writeVarInt: value must be positive");
                throw new ArgumentException();
            }
            else if (value < 0xFD)
            {
                var v = padRight(value.ToByteArray().Take(1), 1);
                return source.Concat(v);
            }
            else if (value <= 0xFFFF) // 0xff, need to pad 1 0x00
            {
                byte[] length = new byte[] { 0xFD };
                var v = padRight(value.ToByteArray().Take(2), 2);
                return source.Concat(length).Concat(v);
            }
            else if (value <= 0XFFFFFFFF) //0xffffff, need to pad 1 0x00
            {
                byte[] length = new byte[] { 0xFE };
                var v = padRight(value.ToByteArray().Take(4), 4);
                return source.Concat(length).Concat(v);
            }
            else //0x ff ff ff ff ff, need to pad 3 0x00
            {
                byte[] length = new byte[] { 0xFF };
                var v = padRight(value.ToByteArray().Take(8), 8);
                return source.Concat(length).Concat(v);
            }
        }

        private static byte[] WriteVarBytes(byte[] value, byte[] Source)
        {
            return writeVarInt(value.Length, Source).Concat(value);
        }

        // add padding zeros on the right
        private static byte[] padRight(byte[] value, int length)
        {
            var l = value.Length;

            Assert(l <= length, "padRight: buffer length exceeded");

            for (int i = 0; i < length - l; i++)
            {
                value = value.Concat(new byte[] { 0x00 });
            }
            return value;
        }

        /// <summary>
        /// Tested, 将Biginteger转换成byteArray, 会在首位添加符号位0x00
        /// </summary>
        /// <param name="number"></param>
        /// <returns></returns>
        private static byte[] ConvertBigintegerToByteArray(BigInteger number)
        {
            var temp = (byte[])(object)number;
            byte[] vs = temp.ToByteString().ToByteArray().Reverse();//ToByteString修改虚拟机类型， Reverse转换端序
            return vs;
        }

        /*
        * Callbacks
        */

        public static void OnNEP17Payment(UInt160 from, BigInteger amount, object data)
        {
            // no-op
            return;
        }

        /*
        * Helpers
        */

        private static void Assert(bool condition, string msg)
        {
            if (!condition)
            {
                notify((ByteString)"CrossChainProxy: ".ToByteArray().Concat(msg.ToByteArray()));
                throw new InvalidOperationException(msg);
            }
        }
    }
}
