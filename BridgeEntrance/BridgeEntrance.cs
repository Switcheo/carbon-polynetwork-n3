using System;
using System.ComponentModel;
using System.Numerics;

using Neo;
using Neo.SmartContract;
using Neo.SmartContract.Framework;
using Neo.SmartContract.Framework.Attributes;
using Neo.SmartContract.Framework.Native;
using Neo.SmartContract.Framework.Services;

namespace BridgeEntrance
{
    [ContractPermission("*", "*")]
    [DisplayName("BridgeEntrance")]
    [ManifestExtra("Author", "Switcheo Labs")]
    [ManifestExtra("Email", "engineering@switcheo.network")]
    [ManifestExtra("Description", "This is the BridgeEntrance Contract for PolyNetwork")]
    public class BridgeEntrance : SmartContract
    {
        // Constants
        // little endian
        // MainNet: 0x5ba6c543c5a86a85e9ab3f028a4ad849b924fab9
        // TestNet: 0x618d44dc3af16c6120dbf65402024f40a04f772a
        // SwitcheoDevNet: 0x1ad744e7f33e3063dde6fa502413af25f3ad6726
        [InitialValue("0x5ba6c543c5a86a85e9ab3f028a4ad849b924fab9", ContractParameterType.Hash160)] // SwitcheoDevNet
        private static readonly byte[] CCMCScriptHash = default;

        // little endian
        // MainNet: 0x8eb3bdf5ed4ac1516d316c6b1b207a3cf5eb7567
        // TestNet: 0xeeebee7ef57cb2106fbad2c51c5b9b4c30f0c0ca
        [InitialValue("0x8eb3bdf5ed4ac1516d316c6b1b207a3cf5eb7567", ContractParameterType.Hash160)]
        private static readonly byte[] LockProxyHash = default;

        // SwitcheoDevNet: NUVHSYuSqAHHNpVyeVR2KkggHNiw5DD2nN
        // Mainnet: NUmfDHK5qeaHhsLwjB5quxAHCL7zsx2VcT
        // hx: NQRWwjMnDXioWGeCTk1RQtDAgjbQuXF5U7
        [InitialValue("NUmfDHK5qeaHhsLwjB5quxAHCL7zsx2VcT", ContractParameterType.Hash160)]
        private static readonly UInt160 owner = default;

        // Storage Locations
        private static readonly byte[] ownerKey = new byte[] { 0x01, 0x01 };
        private static readonly byte[] operatorKey = new byte[] { 0x01, 0x02 };
        private static readonly byte[] pauseKey = new byte[] { 0x01, 0x03 };
        private static readonly BigInteger thisChainId = 14; // TestNet: 88 | MainNet: 14
        private static readonly BigInteger targetChainId = 5; // SwitcheoDevNet: 199 | MainNet: 5

        // Events
        public static event Action<byte[], UInt160> DeployEvent;
        public static event Action<UInt160> TransferOwnershipEvent;
        public static event Action<UInt160, BigInteger, byte[], byte[], byte[]> LockEvent;
        public static event Action<object> notify;

        public static void _deploy(object data, bool update) 
        {
            Storage.Put(Storage.CurrentContext, ownerKey, owner);
            DeployEvent(ownerKey, owner);
        }

        public static bool IsOwner() {
            UInt160 contractOwner = (UInt160)Storage.Get(Storage.CurrentContext, ownerKey);
            return Runtime.CheckWitness(contractOwner);
        }

        public static bool TransferOwnerShip(UInt160 newOwner) 
        {
            Assert(newOwner.Length == 20, "transferOwnership: newOwner must be 20-byte long");
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

        public static bool Update(ByteString nefFile, string manifest) 
        {
            Assert(IsOwner(), "update: not owner");
            ContractManagement.Update(nefFile, manifest, null);
            return true;
        }

        /*
        * Operator
        */

        public static bool IsOperator(UInt160 operatorAddress) 
        {
            ByteString result = Storage.Get(Storage.CurrentContext, operatorKey.Concat(operatorAddress));
            if (result is null) {
                return false;
            } else {
                return Runtime.CheckWitness(operatorAddress);
            }
        }

        public static bool Pause(UInt160 operatorAddress) 
        {
            Assert(IsOwner() || IsOperator(operatorAddress), "pause: not owner or operator");
            Storage.Put(Storage.CurrentContext, pauseKey, new byte[] { 0x01 });
            return true;
        }

        public static bool Unpause(UInt160 operatorAddress) 
        {
            Assert(IsOwner() || IsOperator(operatorAddress), "unpause: not owner or operator");
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

        public static bool Lock(UInt160 fromAssetAddress, UInt160 fromAddress, byte[] recoveryAddress, byte[] toAssetDenom, byte[] toAddress, BigInteger amount, BigInteger feeAmount, BigInteger callAmount, byte[] feeAddress)
        {
            Assert(!IsPaused(), "lock: proxy is paused");
            Assert(fromAssetAddress.Length == 20, "lock: fromAssetAddress must be 20-byte long");
            Assert(fromAddress.Length == 20, "lock: fromAddress must be 20-byte long");
            Assert(recoveryAddress.Length > 0, "lock: recoveryAddress cannot be empty");
            Assert(toAssetDenom.Length > 0, "lock: toAssetDenom cannot be empty");
            Assert(toAddress.Length > 0, "lock: toAddress cannot be empty");
            Assert(amount > 0, "lock: amount must be greater than 0");
            Assert(callAmount > 0, "lock: callAmount must be greater than 0");
            Assert(feeAmount >= 0, "lock: feeAmount must be positive");
            Assert(feeAmount == 0 || feeAddress.Length > 0, "lock: feeAddress cannot be empty if feeAmount is not zero");
            Assert(feeAmount != 0 || feeAddress.Length == 0, "lock: feeAmount cannot be zero if feeAddress is not empty");
            Assert(!fromAddress.Equals(Runtime.ExecutingScriptHash), "lock: can not lock self");

            // get proxy contract on target chain
            var toProxyAddress = GetProxyAddress(fromAssetAddress);
            Assert(toProxyAddress is not null, "lock: toProxyAddress is not registered");

            // get asset denom
            var fromAssetDenom = GetAssetAddress(fromAssetAddress);
           Assert(fromAssetDenom is not null, "lock: toAssetAddress is not registered");

            // transfer asset from fromAddress to proxy contract address, use dynamic call to call nep17 token's contract "transfer"
            bool success = (bool)Contract.Call((UInt160)fromAssetAddress, "transfer", CallFlags.All, new object[] { fromAddress, Runtime.ExecutingScriptHash, callAmount, null });
            Assert(success, "lock: failed to transfer NEP17 token to BridgeEntrance");

            // construct args for ccm call
            var inputArgs = SerializeLockArgs((byte[])fromAssetAddress, fromAssetDenom, toAssetDenom, recoveryAddress, toAddress, amount, feeAmount, feeAddress);

            success = (bool)Contract.Call((UInt160)CCMCScriptHash, "crossChain", CallFlags.All, new object[] { targetChainId, (byte[])toProxyAddress, "unlock".ToByteArray(), inputArgs });
            Assert(success, "lock: failed to call CCMC");

            LockEvent(fromAssetAddress, targetChainId, fromAssetDenom, recoveryAddress, inputArgs);

            return true;
        }

        /*
        * Getters
        */

        // get target proxy contract address according local asset hash
        public static UInt160 GetProxyAddress(UInt160 thisAssetAddress)
        {
            UInt160 addr = (UInt160)Contract.Call((UInt160)LockProxyHash, "getProxyAddress", CallFlags.All, thisAssetAddress); 
            return addr;
        }

        public static byte[] GetAssetAddress(UInt160 thisAssetAddress)
        {
            byte[] addr = (byte[])Contract.Call((UInt160)LockProxyHash, "getAssetAddress", CallFlags.All, thisAssetAddress);
            return addr;
        }

        /*
        * Serialization
        */

        private static byte[] SerializeLockArgs(byte[] fromAssetAddress, byte[] fromAssetDenom, byte[] toAssetDenom, byte[] recoveryAddress,
          byte[] toAddress, BigInteger amount, BigInteger feeAmount, byte[] feeAddress)
        {
            var buffer = new byte[] { };
            buffer = WriteVarBytes(fromAssetAddress, buffer);
            buffer = WriteVarBytes(fromAssetDenom, buffer);
            buffer = WriteVarBytes(toAssetDenom, buffer);
            buffer = WriteVarBytes(recoveryAddress, buffer);
            buffer = WriteVarBytes(toAddress, buffer);
            buffer = WriteUint255(amount, buffer);
            buffer = WriteUint255(feeAmount, buffer);
            buffer = WriteVarBytes(feeAddress, buffer);
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
            else if (value == 0)
            {
                return source.Concat(new byte[] { 0x00 });
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
        private static byte[] WriteVarBytes(byte[] value, byte[] source) 
        {
            return writeVarInt(value.Length, source).Concat(value);
        }

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

        /*
        * Helpers
        */

        private static void Assert(bool condition, string msg)
        {
            if (!condition)
            {
                notify((ByteString)"BridgeEntrance: ".ToByteArray().Concat(msg.ToByteArray()));
                throw new InvalidOperationException(msg);
            }
        }
    }
}
