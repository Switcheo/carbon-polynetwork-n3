# Carbon - Neo3 Contracts

This is the repository contains the  Neo (3.0) deposit and wrapped native token (SWTH) contracts for Carbon via the Polynetwork bridge.

## Deployment

1. Install [.NET 6.0 SDK](https://dotnet.microsoft.com/download)
2. Install contract compiler and templates: `dotnet new -i Neo3.SmartContract.Templates`
3. Compile with: `dotnet build`
4. Install [neo-cli](https://docs.neo.org/docs/en-us/node/cli/setup.html)
5. Sync to latest height with: `dotnet neo-cli.dll`
6. In neo-cli, create / open a wallet: `open wallet`
7. In neo-cli, deploy with: `deploy <pathToSolution>/CrossChainProxy/bin/sc/CrossChainProxy.nef <pathToSolution>/CrossChainProxy/bin/sc/CrossChainProxy.manifest.json`

For more information, see the [Neo 3.0 docs](https://docs.neo.org/docs/en-us/gettingstarted/develop.html).

### Alternate Deployment

Using deployer project:

1. `dotnet build`
2. `CONTRACT_NAME=CrossChainProxy DEPLOYER_PKEY=xxx dotnet Deployer/bin/Debug/net6.0/Deployer.dll`

## Current deployed contracts

### Testnet

- CrossChainManager: [0x1ad744e7f33e3063dde6fa502413af25f3ad6726](https://neo3.testnet.neotube.io/contract/0x1ad744e7f33e3063dde6fa502413af25f3ad6726)
- CrossChainProxy: [0xeeebee7ef57cb2106fbad2c51c5b9b4c30f0c0ca](https://neo3.testnet.neotube.io/contract/0xeeebee7ef57cb2106fbad2c51c5b9b4c30f0c0ca)
- SWTH (NEP-17): [0x285b332bc0323bc334987bd4735fb39cc3269e20](https://neo3.testnet.neotube.io/contract/0x285b332bc0323bc334987bd4735fb39cc3269e20)

### Mainnet

- CrossChainManager: N/A
- CrossChainProxy: [0x974ea0aaec75ed15d80cc0b6077479ab0e8e0e6f](https://dora.coz.io/contract/neo3/mainnet/0x974ea0aaec75ed15d80cc0b6077479ab0e8e0e6f)
- SWTH (NEP-17): [0x78e1330db47634afdb5ea455302ba2d12b8d549f](https://dora.coz.io/contract/neo3/mainnet/0x78e1330db47634afdb5ea455302ba2d12b8d549f)

### Legacy Contracts

For Neo 2.0 contracts, check the [Neo 2.0 repo](https://github.com/Switcheo/carbon-polynetwork-neo).
