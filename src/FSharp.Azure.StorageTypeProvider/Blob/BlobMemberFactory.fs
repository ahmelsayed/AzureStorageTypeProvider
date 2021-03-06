﻿/// Generates top-level blob containers folders.
module internal FSharp.Azure.StorageTypeProvider.Blob.BlobMemberFactory

open FSharp.Azure.StorageTypeProvider.Blob.BlobRepository
open ProviderImplementation.ProvidedTypes
open System
open Microsoft.WindowsAzure.Storage.Blob

let rec private createBlobItem (domainType : ProvidedTypeDefinition) connectionString containerName fileItem = 
    match fileItem with
    | Folder(path, name, getContents) -> 
        let folderProp = ProvidedTypeDefinition((sprintf "%s.%s" containerName path), Some typeof<BlobFolder>, HideObjectMethods = true)
        domainType.AddMember(folderProp)
        folderProp.AddMembersDelayed(fun _ -> 
            (getContents()
             |> Array.choose (createBlobItem domainType connectionString containerName)
             |> Array.toList))
        Some <| ProvidedProperty(name, folderProp, GetterCode = fun _ -> <@@ ContainerBuilder.createBlobFolder connectionString containerName path @@>)
    | Blob(path, name, properties) -> 
        let fileTypeDefinition = 
            match properties.BlobType, path with
            | BlobType.PageBlob, _ -> "PageBlob"
            | _, ContainerBuilder.XML -> "XmlBlob"
            | _, ContainerBuilder.Binary | _, ContainerBuilder.Text -> "BlockBlob"
            |> fun typeName -> domainType.GetMember(typeName).[0] :?> ProvidedTypeDefinition

        match properties.BlobType, properties.Length with
        | _, 0L -> None
        | BlobType.PageBlob, _ -> Some <| ProvidedProperty(name, fileTypeDefinition, GetterCode = fun _ -> <@@ ContainerBuilder.createPageBlobFile connectionString containerName path @@>)
        | BlobType.BlockBlob, _ -> Some <| ProvidedProperty(name, fileTypeDefinition, GetterCode = fun _ -> <@@ ContainerBuilder.createBlockBlobFile connectionString containerName path @@>)
        | _ -> None

let private createContainerType (domainType : ProvidedTypeDefinition) connectionString (container : LightweightContainer) = 
    let individualContainerType = ProvidedTypeDefinition(container.Name + "Container", Some typeof<BlobContainer>, HideObjectMethods = true)
    individualContainerType.AddXmlDoc <| sprintf "Provides access to the '%s' container." container.Name
    individualContainerType.AddMembersDelayed(fun _ -> 
        (container.GetFiles()
         |> Seq.choose (createBlobItem domainType connectionString container.Name)
         |> Seq.toList))
    domainType.AddMember(individualContainerType)
    // this local binding is required for the quotation.
    let containerName = container.Name
    let containerProp = 
        ProvidedProperty(container.Name, individualContainerType, GetterCode = fun _ -> <@@ ContainerBuilder.createContainer connectionString containerName @@>)
    containerProp.AddXmlDocDelayed(fun () -> sprintf "Provides access to the '%s' container." containerName)
    containerProp

/// Builds up the Blob Storage container members
let getBlobStorageMembers (connectionString, domainType : ProvidedTypeDefinition) = 
    let containerListingType = ProvidedTypeDefinition("Containers", Some typeof<obj>, HideObjectMethods = true)
    containerListingType.AddMembersDelayed(fun _ -> getBlobStorageAccountManifest (connectionString) |> List.map (createContainerType domainType connectionString))
    domainType.AddMember containerListingType

    let cbcProp = ProvidedProperty("CloudBlobClient", typeof<CloudBlobClient>, GetterCode = (fun _ -> <@@ ContainerBuilder.createBlobClient connectionString @@>))
    cbcProp.AddXmlDoc "Gets a handle to the Blob Azure SDK client for this storage account."
    containerListingType.AddMember(cbcProp)

    let containerListingProp = ProvidedProperty("Containers", containerListingType, GetterCode = (fun _ -> <@@ () @@>), IsStatic = true)
    containerListingProp.AddXmlDoc "Gets the list of all containers in this storage account."
    containerListingProp