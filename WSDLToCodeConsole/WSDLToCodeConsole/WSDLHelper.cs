namespace WSDLToCodeConsole
{
    using System;
    using System.CodeDom;
    using System.CodeDom.Compiler;
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Reflection;
    using System.ServiceModel.Description;
    using System.Text;
    using System.Web.Services.Description;
    using System.Xml.Schema;
    using Microsoft.CSharp;

    public static class WSDLHelper
    {
        public static ServiceDescriptionImporter GetServiceDescriptionImporter(Uri wsdlUri, bool soap12 = false)
        {
            System.Web.Services.Description.ServiceDescription sd = null;
            if (wsdlUri.IsFile && !wsdlUri.IsUnc)
            {
                sd = System.Web.Services.Description.ServiceDescription.Read(wsdlUri.LocalPath);
            }
            else
            {
                WebClient wc = new WebClient();
                Stream stream = wc.OpenReadTaskAsync(wsdlUri).Result;
                sd = System.Web.Services.Description.ServiceDescription.Read(stream);
            }
            ServiceDescriptionImporter sdi = new ServiceDescriptionImporter();
            if (soap12)
            {
                sdi.ProtocolName = "Soap12";
            }
            sdi.AddServiceDescription(sd, null, null);
            return sdi;
        }

        public static Assembly CreateAssemblyBySDI(Uri wsdlUri, bool soap12 = false)
        {
            ServiceDescriptionImporter sdi = GetServiceDescriptionImporter(wsdlUri, soap12);
            CodeNamespace cn = new CodeNamespace();
            CodeCompileUnit ccu = new CodeCompileUnit();
            ccu.Namespaces.Add(cn);
            sdi.Import(cn, ccu);
            CSharpCodeProvider csc = new CSharpCodeProvider();

            CompilerParameters cplist = new CompilerParameters(new string[] { "System.dll", "System.Data.dll", "System.XML.dll", "System.ServiceModel.dll", "System.Web.Services.dll", "System.Runtime.Serialization.dll" });
            cplist.GenerateExecutable = false;
            cplist.GenerateInMemory = true;

            CompilerResults cr = csc.CompileAssemblyFromDom(cplist, ccu);
            if (true == cr.Errors.HasErrors)
            {
                System.Text.StringBuilder sb = new System.Text.StringBuilder();
                foreach (System.CodeDom.Compiler.CompilerError ce in cr.Errors)
                {
                    sb.Append(ce.ToString());
                    sb.Append(System.Environment.NewLine);
                }
                throw new Exception(sb.ToString());
            }
            Assembly assembly = cr.CompiledAssembly;
            return assembly;
        }

        public static bool CreateCSharpBySDI(Uri wsdlUri, string outputfilepath)
        {
            WebClient wc = new WebClient();
            Stream stream = wc.OpenReadTaskAsync(wsdlUri).Result;
            System.Web.Services.Description.ServiceDescription sd = System.Web.Services.Description.ServiceDescription.Read(stream);
            ServiceDescriptionImporter sdi = new ServiceDescriptionImporter();
            sdi.AddServiceDescription(sd, null, null);
            foreach (XmlSchema wsdlSchema in sd.Types.Schemas)
            {
                // Loop through all detected imports in the main schema
                foreach (XmlSchemaObject externalSchema in wsdlSchema.Includes)
                {
                    // Read each external schema into a schema object and add to importer
                    if (externalSchema is XmlSchemaImport)
                    {
                        Uri baseUri = wsdlUri;
                        Uri schemaUri = new Uri(baseUri, ((XmlSchemaExternal)externalSchema).SchemaLocation);

                        Stream schemaStream = wc.OpenRead(schemaUri);
                        XmlSchema schema = XmlSchema.Read(schemaStream, null);
                        sdi.Schemas.Add(schema);
                    }
                }
            }

            // set up for code generation by creating a namespace and adding to importer
            CodeNamespace ns = new CodeNamespace();
            CodeCompileUnit ccu = new CodeCompileUnit();
            ccu.Namespaces.Add(ns);
            sdi.Import(ns, ccu);

            // final code generation in specified language
            CSharpCodeProvider provider = new CSharpCodeProvider();
            IndentedTextWriter tw = new IndentedTextWriter(new StreamWriter(outputfilepath));
            provider.GenerateCodeFromCompileUnit(ccu, tw, new CodeGeneratorOptions());
            tw.Close();
            return File.Exists(outputfilepath);
        }

        public static MetadataSet GetMetadata(Uri wsdlUri)
        {
            MetadataExchangeClientMode mode = MetadataExchangeClientMode.HttpGet;
            if (wsdlUri.IsFile && !wsdlUri.IsUnc)
            {
                mode = MetadataExchangeClientMode.MetadataExchange;
            }
            MetadataExchangeClient client = new MetadataExchangeClient(wsdlUri, mode);
            MetadataSet result = client.GetMetadataAsync().Result;
            return result;
        }

        public static bool CreateCSharpByMetadataSet(MetadataSet metadataSet, string outputfilepath)
        {
            WsdlImporter importer = new WsdlImporter(metadataSet);
            ServiceContractGenerator generator = new ServiceContractGenerator();
            ServiceEndpointCollection allEndpoints = importer.ImportAllEndpoints();
            Collection<ContractDescription> contacts = importer.ImportAllContracts();
            foreach (ContractDescription contract in contacts)
            {
                //ReplyAction="*" for an OperationContract means the WsdlExporter (which publishes the metadata) will ignore that Operation.
                //Setting any other value will fix it
                if (contract.Operations != null)
                {
                    foreach (OperationDescription operation in contract.Operations)
                    {
                        if (operation.Messages != null)
                        {
                            MessageDescription outmessage = operation.Messages.FirstOrDefault(item => { return item.Direction == MessageDirection.Output && !string.IsNullOrEmpty(item.Action) && item.Action.Equals("*", StringComparison.OrdinalIgnoreCase); });
                            if (outmessage != null)
                            {
                                //Relection Set Value
                                PropertyInfo field = typeof(MessageDescription).GetProperty("Action");
                                if (field != null)
                                {
                                    field.SetValue(outmessage, "");
                                }
                            }
                        }
                    }
                }
                generator.GenerateServiceContractType(contract);
            }
            CSharpCodeProvider csc = new CSharpCodeProvider();
            CompilerParameters cplist = new CompilerParameters(new string[] { "System.dll", "System.Data.dll", "System.XML.dll", "System.ServiceModel.dll", "System.Web.Services.dll", "System.Runtime.Serialization.dll" });
            cplist.GenerateExecutable = false;
            cplist.GenerateInMemory = true;

            //compile
            CompilerResults cr = csc.CompileAssemblyFromDom(cplist, generator.TargetCompileUnit);
            if (true == cr.Errors.HasErrors)
            {
                System.Text.StringBuilder sb = new System.Text.StringBuilder();
                foreach (System.CodeDom.Compiler.CompilerError ce in cr.Errors)
                {
                    sb.Append(ce.ToString());
                    sb.Append(System.Environment.NewLine);
                }
                throw new Exception(sb.ToString());
            }

            StringBuilder stringBuilder = new StringBuilder();
            StringWriter stringWriter = new StringWriter(stringBuilder);
            CodeGeneratorOptions options = new CodeGeneratorOptions();
            options.BracingStyle = "C";
            csc.GenerateCodeFromCompileUnit(generator.TargetCompileUnit, stringWriter, options);
            stringWriter.Close();
            File.WriteAllText(outputfilepath, stringBuilder.ToString(), Encoding.UTF8);
            return File.Exists(outputfilepath);
        }

        public static Assembly CreateAssemblyByMetadataSet(MetadataSet metadataSet)
        {
            WsdlImporter importer = new WsdlImporter(metadataSet);
            Collection<ContractDescription> contracts = importer.ImportAllContracts();
            ServiceEndpointCollection allEndpoints = importer.ImportAllEndpoints();
            ServiceContractGenerator serviceContractGenerator = new ServiceContractGenerator();
            var endpointsForContracts = new Dictionary<string, IEnumerable<ServiceEndpoint>>();
            foreach (ContractDescription contract in contracts)
            {
                //ReplyAction="*" for an OperationContract means the WsdlExporter (which publishes the metadata) will ignore that Operation.
                //Setting any other value will fix it
                if (contract.Operations != null)
                {
                    foreach (OperationDescription operation in contract.Operations)
                    {
                        if (operation.Messages != null)
                        {
                            MessageDescription outmessage = operation.Messages.FirstOrDefault(item => { return item.Direction == MessageDirection.Output && !string.IsNullOrEmpty(item.Action) && item.Action.Equals("*", StringComparison.OrdinalIgnoreCase); });
                            if (outmessage != null)
                            {
                                //Relection Set Value
                                PropertyInfo field = typeof(MessageDescription).GetProperty("Action");
                                if (field != null)
                                {
                                    field.SetValue(outmessage, "");
                                }
                            }
                        }
                    }
                }
                serviceContractGenerator.GenerateServiceContractType(contract);
                // Keep a list of each contract's endpoints.
                endpointsForContracts[contract.Name] = allEndpoints.Where(ep => ep.Contract.Name == contract.Name).ToList();
            }
            CodeDomProvider codeDomProvider = CodeDomProvider.CreateProvider("C#");
            CompilerParameters compilerParameters = new CompilerParameters(new string[] { "System.dll", "System.Data.dll", "System.XML.dll", "System.ServiceModel.dll", "System.Web.Services.dll", "System.Runtime.Serialization.dll" });
            compilerParameters.GenerateInMemory = true;
            compilerParameters.GenerateExecutable = false;
            CompilerResults compilerResults = codeDomProvider.CompileAssemblyFromDom(compilerParameters, serviceContractGenerator.TargetCompileUnit);
            if (true == compilerResults.Errors.HasErrors)
            {
                StringBuilder sb = new StringBuilder();
                foreach (CompilerError ce in compilerResults.Errors)
                {
                    sb.Append(ce.ToString());
                    sb.Append(Environment.NewLine);
                }
                throw new Exception(sb.ToString());
            }
            return compilerResults.CompiledAssembly;
        }

        public static bool CreateCSharpByWSDLI(Uri wsdlUri, string outputfilepath)
        {
            MetadataSet mSet = GetMetadata(wsdlUri);
            return CreateCSharpByMetadataSet(mSet, outputfilepath);
        }

        public static Assembly CreateAssemblyByWSDLI(Uri wsdlUri)
        {
            MetadataSet mSet = GetMetadata(wsdlUri);
            Assembly assembly = CreateAssemblyByMetadataSet(mSet);
            return assembly;
        }

    }
}
