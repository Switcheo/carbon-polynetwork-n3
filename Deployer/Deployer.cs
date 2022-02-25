using Neo.Network.P2P.Payloads;
using Neo.Network.RPC;
using Utility = Neo.Network.RPC.Utility;
using Neo.SmartContract;
using Neo.SmartContract.Manifest;
using Neo.Wallets;
using Neo.IO;
using System;
using System.IO;
using System.Text;

namespace Deployer
{
    class Program
    {
        static void Main(string[] args)
        {
            var contractName = Environment.GetEnvironmentVariable("CONTRACT_NAME");
            if (contractName is null) {
              Console.WriteLine("CONTRACT_NAME env variable is missing!");
              return;
            }
            Deploy(contractName).GetAwaiter().GetResult();
            Console.Read();
        }

        private static async Task Deploy(string contractName)
        {
            // choose a neo node with rpc opened, here we use the localhost
            RpcClient client = new RpcClient(new Uri("http://seed1.neo.org:10332"), null, null, Neo.ProtocolSettings.Load("config.json"));
            ContractClient contractClient = new ContractClient(client);

            var pathToFiles = Environment.GetEnvironmentVariable("ARTIFACTS_PATH");
            if (pathToFiles is null) {
              pathToFiles = $"{contractName}/bin/sc";
            }

            string nefFilePath = $"{pathToFiles}/{contractName}.nef";
            string manifestFilePath = $"{pathToFiles}/{contractName}.manifest.json";

            // read nefFile & manifestFile
            NefFile nefFile;
            using (var stream = new BinaryReader(File.OpenRead(nefFilePath), Encoding.UTF8, false))
            {
                nefFile = stream.ReadSerializable<NefFile>();
            }

            ContractManifest manifest = ContractManifest.Parse(File.ReadAllBytes(manifestFilePath));

            // deploying contract needs sender to pay the system fee
            KeyPair deployerKey = Utility.GetKeyPair(Environment.GetEnvironmentVariable("DEPLOYER_PKEY"));

            // create the deploy transaction
            Transaction transaction = await contractClient.CreateDeployContractTxAsync(nefFile.ToArray(), manifest, deployerKey).ConfigureAwait(false);

            // Broadcast the transaction over the NEO network
            await client.SendRawTransactionAsync(transaction).ConfigureAwait(false);
            Console.WriteLine($"Transaction {transaction.Hash.ToString()} is broadcasted!");

            // print a message after the transaction is on chain
            WalletAPI neoAPI = new WalletAPI(client);
            await neoAPI.WaitTransactionAsync(transaction)
               .ContinueWith(async (p) => Console.WriteLine($"Transaction vm state is  {(await p).VMState}"));
        }
    }
}
