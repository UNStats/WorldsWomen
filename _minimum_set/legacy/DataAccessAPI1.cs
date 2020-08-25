using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Configuration;
using System.IO;
using System.Data.OleDb;
using System.Data.SqlClient;
using System.Data;
using System.Text.RegularExpressions;
using RestSharp;
using System.Xml;
using Newtonsoft.Json.Linq;
using System.Threading.Tasks;

namespace MinimumSetDBM
{
    public class DataAccessAPI1
    {

        private RestClient client;
        private RestRequest request;
        private List<CodelistILO> codeListILO;
        private List<CodelistILO> coverageListILO;


        public DataAccessAPI1()
        {
            //    client = new RestClient("https://api.uis.unesco.org");

            //    request = new RestRequest("/sdmx/data/UNESCO,EDU_NON_FINANCE,1.0//LR.PT...F.Y15T24............", Method.GET);
            //    request.AddQueryParameter("subscription-key", "89aa6056ce404764bbc605ea21ec0735");
            //    request.AddQueryParameter("format", "sdmx-json");
            //    request.AddQueryParameter("startPeriod", "1990");
            codeListILO = new List<CodelistILO>();
            coverageListILO = new List<CodelistILO>();

        }

        public DataAccessAPI1(String baseUrl, String path, String returnFormat, String startPeriod, bool hasKey)
        {

            client = new RestClient(baseUrl);

            request = new RestRequest(path, Method.GET);
            if(hasKey)
                request.AddQueryParameter("subscription-key", "89aa6056ce404764bbc605ea21ec0735");
            request.AddQueryParameter("format", returnFormat);
            request.AddQueryParameter("startPeriod", startPeriod);
            codeListILO = new List<CodelistILO>();
        }

        public String GetCodeListfromILO(String baseUrl, String path, String returnFormat)
        {

            RestClient client1 = new RestClient(baseUrl);

            RestRequest request1 = new RestRequest(path, Method.GET);
            request1.AddQueryParameter("format", returnFormat);
            
            IRestResponse response = client1.Execute(request1);
            return response.Content;
        }

        public void resetPeriod(String startPeriod, String endPeriod) //so as to limit the response data size <2000 for UIS we loop in period of 3 years
        {
            bool endModified = false;
            foreach (Parameter p in request.Parameters)
            {
                if(p.Name == "startPeriod")
                {
                    p.Value = startPeriod;
                }
                if (p.Name == "endPeriod")
                {
                    p.Value = endPeriod;
                    endModified = true;
                }
            }
            if(!endModified)
                request.AddQueryParameter("endPeriod", endPeriod);
            
        }

        public String executeRequest()
        {
            IRestResponse response = client.Execute(request);
            
            return response.Content;
        }

        public String hasError(String xmlString)
        {
            
            var task = Task.Factory.StartNew(() =>
            {
                String ret = "0";
                XmlDocument doc = new XmlDocument();
                doc.LoadXml(xmlString);

                XmlNamespaceManager manager = new XmlNamespaceManager(doc.NameTable);
                manager.AddNamespace("mes", "http://www.sdmx.org/resources/sdmxml/schemas/v2_1/message");
               // manager.AddNamespace("ns1", "urn:sdmx:org.sdmx.infomodel.datastructure.Dataflow=UNESCO:EDU_NON_FINANCE(1.0):ObsLevelDim:TIME_PERIOD");

                XmlNode errorNode = doc.DocumentElement.SelectSingleNode("/mes:Error/mes:ErrorMessage", manager);

                if (errorNode != null )
                {
                    ret = errorNode.Attributes["code"].Value.ToString();
                }
                
                return ret;
            });
            return task.Result;
        }

        public List<TransformApiData> transform_SDMX2_1_UIS(String xmlString)
        {

            var task = Task.Factory.StartNew(() =>
            {
                XmlDocument doc = new XmlDocument();
                doc.LoadXml(xmlString);

                XmlNamespaceManager manager = new XmlNamespaceManager(doc.NameTable);
                manager.AddNamespace("message", "http://www.sdmx.org/resources/sdmxml/schemas/v2_1/message");
                manager.AddNamespace("ns1", "urn:sdmx:org.sdmx.infomodel.datastructure.Dataflow=UNESCO:EDU_NON_FINANCE(1.0):ObsLevelDim:TIME_PERIOD");

                //XmlNodeList seriesNodes = doc.DocumentElement.SelectNodes("/message:StructureSpecificData/message:DataSet/ns1:Series", manager);
                XmlNodeList seriesNodes = doc.DocumentElement.SelectNodes("/message:StructureSpecificData/message:DataSet/Series", manager);

                XmlNode preparedNode = doc.DocumentElement.SelectSingleNode("/message:StructureSpecificData/message:Header/message:Prepared", manager);
                XmlNode idNode = doc.DocumentElement.SelectSingleNode("/message:StructureSpecificData/message:Header/message:ID", manager);


                List<TransformApiData> dataList = new List<TransformApiData>();

                countryJson1 = JObject.Parse(jsonCountryStr.Replace(@"\", ""));
               
                foreach (XmlNode seriesNode in seriesNodes)
                {
                    //System.Console.WriteLine("Area: "+seriesNode.Attributes["REF_AREA"].Value);

                    if (seriesNode.Attributes["REF_AREA"].Value.Length == 2 && countryJson1.GetValue(seriesNode.Attributes["REF_AREA"].Value)!=null)
                    {
                        XmlNodeList obsNodes = seriesNode.ChildNodes;//seriesNode.SelectNodes("/ns1:Obs", manager);

                        foreach (XmlNode obsNode in obsNodes)
                        {
                            TransformApiData data = new TransformApiData();
                            data.countryCode = seriesNode.Attributes["REF_AREA"].Value;

                            
                            data.countryCodeUNSD = countryJson1.GetValue(data.countryCode)["m49"].ToString();
                            data.countryName = countryJson1.GetValue(data.countryCode)["name"].ToString();
                            data.referenceYear = obsNode.Attributes["TIME_PERIOD"].Value;
                            if(seriesNode.Attributes["UNIT_MEASURE"].Value == "GPI")
                            {
                                data.sex = "Not applicable";
                            }
                            else
                            {
                                data.sex = seriesNode.Attributes["SEX"].Value == "F" ? "Female" : (seriesNode.Attributes["SEX"].Value == "M" ? "Male" : "Both sexes" );
                            }
                            
                            data.dataValue = obsNode.Attributes["OBS_VALUE"].Value;
                            data.dataPointNature = obsNode.Attributes["OBS_STATUS"].Value == "A" ? "C" : (obsNode.Attributes["OBS_STATUS"].Value == "E"?"E":(obsNode.Attributes["OBS_STATUS"].Value == "Z"?"N":"NA")); //A:Normal, E: EstimatedValue, NA: Not Available
                                                                                                                                                                   

                            data.dataPointOrigin = "NA";
                            data.footNote = "Data extracted via UNESCO API on: "+ preparedNode.InnerText;
                            data.messageId = idNode.InnerText;

                            dataList.Add(data);
                                                        
                        }
                    }
                    //else
                    //{
                    //    int i = 0;
                    //    i++; ;
                        
                    //}
                }
                return dataList;
            });
            
            return task.Result;
        }

        public List<TransformApiData> transform_SDMX2_1_ILO(String xmlString)
        {
            String baseUrl = "http://www.ilo.org";
            String path = "";
            String format = "compact_2_1";
            String[] notesCodes = { "S1", "S2", "S3", "S4", "S5", "S6", "S7", "S8", "S9", "S10", "S11",
                                "T22", "T10", "T3", "T2", "T28", "T29", "T8", "T32", "T34", "T13", "T39", "T36", "T27", "T21", "T19", "T38", "T11", "T6", "T16", "T30", "T17", "T25",
                                "T1","T20","T18","T24","T23","T37","T4","T7","T35","T31","T14","T26","T15","T5","T9","T12","T33",
                                "R1",
                                "C9",  "C91",  "C11", "C92", "C93", "C90", "C12","C10",
                                "I11", "I27",  "I8", "I3", "I29", "I30", "I22", "I17", "I12", "I33", "I20", "I25", "I26", "I2","I18","I10","I32","I23","I13","I31","I14","I15","I16","I28","I1",
                                "I5","I24","I19","I4","I6","I9","I21","I7"};

            //"C1", "C6", "C7", "C5", "C3", "C8", "C4", "C13", "C2"  Non Standart
            //for coverage: [{note_code:'S3', note_description:'reference period' code_id: '5', code_description: 'annual average'}]

            if (coverageListILO == null)
            {
                coverageListILO = new List<CodelistILO>();             
            }
            if (coverageListILO.Count == 0)
            {
                foreach (String noteCode in notesCodes) //for coverage 
                {
                    path = "/ilostat/sdmx/ws/rest/codelist/ILO/CL_NOTE_" + noteCode;
                    String retCL2 = GetCodeListfromILO(baseUrl, path, format);
                    // System.Console.WriteLine("retCL2 " + retCL2.Length);
                    if (retCL2.Length > 0)
                    {
                        List<CodelistILO> cl1 = transform_SDMX2_1_ILO_Coveragelist(noteCode, retCL2);
                        coverageListILO.AddRange(cl1);
                    }
                }
                //for (int i = 1; i <= 11; i++) //for coverage 
                //{
                //    path = "/ilostat/sdmx/ws/rest/codelist/ILO/CL_NOTE_S" + i.ToString();
                //    String retCL2 = GetCodeListfromILO(baseUrl, path, format);
                //    // System.Console.WriteLine("retCL2 " + retCL2.Length);
                //    if (retCL2.Length > 0)
                //    {
                //        List<CodelistILO> cl1 = transform_SDMX2_1_ILO_Coveragelist("S" + i.ToString(), retCL2);
                //        coverageListILO.AddRange(cl1);
                //    }
                //}
            }

            //System.Console.WriteLine("Total CoveragelistNodes: " + coverageListILO.Count);

            var task = Task.Factory.StartNew(() =>
            {
                XmlDocument doc = new XmlDocument();
                doc.LoadXml(xmlString);

                XmlNamespaceManager manager = new XmlNamespaceManager(doc.NameTable);
                manager.AddNamespace("message", "http://www.sdmx.org/resources/sdmxml/schemas/v2_1/message");
                manager.AddNamespace("ns1", "urn:sdmx:org.sdmx.infomodel.datastructure.Dataflow=ILO:DF_YI_CAN_EAP_DWAP_SEX_AGE_RT(1.0):ObsLevelDim:TIME_PERIOD");

                XmlNodeList seriesNodes = doc.DocumentElement.SelectNodes("/message:StructureSpecificData/message:DataSet/Series", manager);

                XmlNode preparedNode = doc.DocumentElement.SelectSingleNode("/message:StructureSpecificData/message:Header/message:Prepared", manager);
                XmlNode idNode = doc.DocumentElement.SelectSingleNode("/message:StructureSpecificData/message:Header/message:ID", manager);


                List<TransformApiData> dataList = new List<TransformApiData>();

                countryJson1 = JObject.Parse(jsonCountryStr.Replace(@"\", ""));
                countryJsonISO = JObject.Parse(jsonIso3Iso2);
                

                foreach (XmlNode seriesNode in seriesNodes)
                {
                   // System.Console.WriteLine("Area: "+seriesNode.Attributes["COUNTRY"].Value);

                    if (seriesNode.Attributes["COUNTRY"].Value.Length == 3 && countryJsonISO.GetValue(seriesNode.Attributes["COUNTRY"].Value)!=null)
                    {
                        XmlNodeList obsNodes = seriesNode.ChildNodes;//seriesNode.SelectNodes("/ns1:Obs", manager);

                        String iso2 = countryJsonISO.GetValue(seriesNode.Attributes["COUNTRY"].Value).ToString();

                        foreach (XmlNode obsNode in obsNodes)
                        {
                            TransformApiData data = new TransformApiData();
                            data.countryCode = seriesNode.Attributes["COUNTRY"].Value;
                            data.surveyNo = seriesNode.Attributes["SURVEY"].Value;
                            
                            data.countryCodeUNSD = countryJson1.GetValue(iso2)["m49"].ToString();
                            data.countryName = countryJson1.GetValue(iso2)["name"].ToString();
                            

                            data.referenceYear = obsNode.Attributes["TIME_PERIOD"].Value;
                            if (obsNode.Attributes["UNIT_MEAS"].Value == "GPI") //?check this for ILO case?
                            {
                                data.sex = "Not applicable";
                            }
                            else
                            {
                                data.sex = seriesNode.Attributes["CLASSIF_SEX"].Value == "SEX_F" ? "Female" : (seriesNode.Attributes["CLASSIF_SEX"].Value == "SEX_M" ? "Male" : "Both sexes");
                            }

                            data.dataValue = obsNode.Attributes["OBS_VALUE"].Value;
                            data.dataPointNature = "C"; 
                            
                            data.dataPointOrigin = "N";
                            //data.footNote = "Data extracted via ILO API on: " + preparedNode.InnerText;
                            data.messageId = idNode.InnerText;
                            foreach (String noteCode in notesCodes)
                            {
                                if (obsNode.Attributes["MET_" + noteCode] != null)
                                {
                                    foreach (CodelistILO codelist in coverageListILO)
                                    {
                                        if (codelist.note_code == noteCode && codelist.code_id == obsNode.Attributes["MET_"+ noteCode].Value )
                                        {
                                            if(noteCode.Substring(0, 1) == "S")//for coverage
                                            {
                                                data.coverage += codelist.note_description + ": " + codelist.code_description + " | ";
                                            }
                                            else if (data.footNote == null || data.footNote.Length <= 0)
                                            {
                                                data.footNote = codelist.note_description + ": " + codelist.code_description;
                                            }
                                            else if (data.footNote1==null || data.footNote1.Length<=0)
                                            {
                                                data.footNote1 = codelist.note_description + ": " + codelist.code_description;
                                            }
                                            else if (data.footNote2 == null || data.footNote2.Length <= 0)
                                            {
                                                data.footNote2 = codelist.note_description + ": " + codelist.code_description;
                                            }
                                            else if (data.footNote3 == null || data.footNote3.Length <= 0)
                                            {
                                                data.footNote3 = codelist.note_description + ": " + codelist.code_description;
                                            }
                                            else if (data.footNote4 == null || data.footNote4.Length <= 0)
                                            {
                                                data.footNote4 = codelist.note_description + ": " + codelist.code_description;
                                            }
                                            else if (data.footNote5 == null || data.footNote5.Length <= 0)
                                            {
                                                data.footNote5 = codelist.note_description + ": " + codelist.code_description;
                                            }
                                            break;
                                        }
                                    }
                                }

                            }
                            //for (int i=1; i <= 11; i++) //for coverage
                            //{
                            //    if (obsNode.Attributes["MET_S" + i] != null)
                            //    {
                            //        foreach (CodelistILO codelist in coverageListILO)
                            //        {
                            //            if (codelist.note_code == "S" + i && codelist.code_id == obsNode.Attributes["MET_S" + i].Value)
                            //            {
                            //                data.coverage += codelist.note_description + ":" + codelist.code_description + " | ";
                            //                break;
                            //            }
                            //        }
                            //    }
                            //}
                            if (data.coverage!=null && data.coverage.Length > 1)
                            {
                                data.coverage = data.coverage.Substring(0, data.coverage.Length - 2);
                            }

                            dataList.Add(data);

                        }
                    }
                }
                return dataList;
            });

            
            List<TransformApiData> sortedResult = task.Result.OrderBy(o => o.countryCode).ThenBy(o => o.referenceYear).ToList();
            List<TransformApiData> finalResult = new List<TransformApiData>();
           // System.Console.WriteLine("sortedResult:" + sortedResult.Count);
            foreach (TransformApiData transformILOApiData in sortedResult)
            {
                if(finalResult.Count>0 
                    && transformILOApiData.countryCode == finalResult.Last().countryCode
                    && transformILOApiData.referenceYear == finalResult.Last().referenceYear)
                { // get codelist with sort order and select smallest sortorder record
                    //URL: codelist/ILO/CL_SURVEY_CTRYCODE?format=compact_2_1
                    // store retrieved record in var for future reference. {ctry: }

                    //check if country already was clled and is present in codeListILO, else call API.
                    bool existsCodeListCtry = false;
                    foreach(CodelistILO codelist in codeListILO)
                    {
                        if(codelist.country == transformILOApiData.countryCode)
                        {
                            existsCodeListCtry = true;
                            break;
                        }
                    }
                    if (!existsCodeListCtry)
                    {
                        String retCL = GetCodeListfromILO(baseUrl, "/ilostat/sdmx/ws/rest/codelist/ILO/CL_SURVEY_" + transformILOApiData.countryCode, format);
                        List<CodelistILO> cl = transform_SDMX2_1_ILO_Codelist(transformILOApiData.countryCode, retCL);
                        codeListILO.AddRange(cl);
                    }
                    String newSeriesSort = "";
                    String oldSeriesSort = "";
                    foreach (CodelistILO codelist in codeListILO)
                    {
                        if (codelist.country == transformILOApiData.countryCode && transformILOApiData.surveyNo == codelist.surveyNo)
                        {
                            newSeriesSort = codelist.sortVal;
                        }
                        if (codelist.country == transformILOApiData.countryCode && finalResult.ElementAt(finalResult.Count-1).surveyNo == codelist.surveyNo)
                        {
                            oldSeriesSort = codelist.sortVal;
                        }
                    } 
                    if(int.Parse(oldSeriesSort) > int.Parse(newSeriesSort))
                    {
                        finalResult.RemoveAt(finalResult.Count - 1);
                        finalResult.Add(transformILOApiData);
                    }

                } else
                    finalResult.Add(transformILOApiData);
                
                
            }
           // System.Console.WriteLine("finalResult:" + finalResult.Count);
            return finalResult;
        }

        public List<CodelistILO> transform_SDMX2_1_ILO_Codelist(String ctry, String xmlString)
        {
            //System.Console.WriteLine("########transform_SDMX2_1_ILO_Codelist################");
            var task = Task.Factory.StartNew(() =>
            {
                XmlDocument doc = new XmlDocument();
                doc.LoadXml(xmlString);

                XmlNamespaceManager manager = new XmlNamespaceManager(doc.NameTable);
                manager.AddNamespace("mes", "http://www.sdmx.org/resources/sdmxml/schemas/v2_1/message");
                manager.AddNamespace("str", "http://www.sdmx.org/resources/sdmxml/schemas/v2_1/structure");
                manager.AddNamespace("com", "http://www.sdmx.org/resources/sdmxml/schemas/v2_1/common");
                
                XmlNodeList codes = doc.DocumentElement.SelectNodes("/mes:Structure/mes:Structures/str:Codelists/str:Codelist/str:Code", manager);
                List<CodelistILO> dataList = new List<CodelistILO>();
                //System.Console.WriteLine("codeLists::" + codeLists.Count);
                foreach (XmlNode code in codes)
                {

                    XmlNodeList annotationsNode = code["com:Annotations"].ChildNodes; //com:Annotations and com:Name
                    //System.Console.WriteLine("annotationsNode" + annotationsNode.Count);
                    foreach (XmlNode annotationNode in annotationsNode)
                    {
                        if (annotationNode.ChildNodes[0].InnerText == "Sort")
                        {
                            CodelistILO data = new CodelistILO();
                            data.country = ctry;
                            data.surveyNo = code.Attributes["id"].Value;
                            data.sortVal = annotationNode.ChildNodes[1].InnerText;
                            dataList.Add(data);
                        }
                    }
                }
                return dataList;
            });
            return task.Result;
        }

        public List<CodelistILO> transform_SDMX2_1_ILO_Coveragelist(String note_code, String xmlString)
        {
            //System.Console.WriteLine("########transform_SDMX2_1_ILO_Codelist################");
            var task = Task.Factory.StartNew(() =>
            {
                XmlDocument doc = new XmlDocument();
                doc.LoadXml(xmlString);

                XmlNamespaceManager manager = new XmlNamespaceManager(doc.NameTable);
                manager.AddNamespace("mes", "http://www.sdmx.org/resources/sdmxml/schemas/v2_1/message");
                manager.AddNamespace("str", "http://www.sdmx.org/resources/sdmxml/schemas/v2_1/structure");
                manager.AddNamespace("com", "http://www.sdmx.org/resources/sdmxml/schemas/v2_1/common");
                
                XmlNodeList codes = doc.DocumentElement.SelectNodes("/mes:Structure/mes:Structures/str:Codelists/str:Codelist/str:Code", manager);
                List<CodelistILO> dataList = new List<CodelistILO>();
              //  System.Console.WriteLine("codes.Count::" + codes.Count);
                foreach (XmlNode code in codes)
                {
                    CodelistILO data = new CodelistILO();
                    data.note_code = note_code;
                    data.note_description = code.ParentNode.SelectNodes("com:Name[@xml:lang='en']", manager)[0].InnerText;
                    data.code_id = code.Attributes["id"].Value;
                    data.code_description = code.SelectNodes("com:Name[@xml:lang='en']", manager)[0].InnerText;
                    //System.Console.WriteLine("data.note_description::" + code.SelectNodes("com:Name[@xml:lang='en']", manager)[0].InnerText);

                    dataList.Add(data);
                }
                return dataList;
            });
            return task.Result;
        }

        private static String jsonCountryStr = "{'AD':{'m49':20,'name':\"Andorra\"},'AE':{'m49':784,'name':\"United Arab Emirates\"},'AF':{'m49':4,'name':\"Afghanistan\"},'AG':{'m49':28,'name':\"Antigua and Barbuda\"},'AI':{'m49':660,'name':\"Anguilla\"},'AL':{'m49':8,'name':\"Albania\"},'AM':{'m49':51,'name':\"Armenia\"},'AO':{'m49':24,'name':\"Angola\"},'AR':{'m49':32,'name':\"Argentina\"},'AS':{'m49':16,'name':\"American Samoa\"},'AT':{'m49':40,'name':\"Austria\"},'AU':{'m49':36,'name':\"Australia\"},'AW':{'m49':533,'name':\"Aruba\"},'AX':{'m49':248,'name':\"Åland Islands\"},'AZ':{'m49':31,'name':\"Azerbaijan\"},'BA':{'m49':70,'name':\"Bosnia and Herzegovina\"},'BB':{'m49':52,'name':\"Barbados\"},'BD':{'m49':50,'name':\"Bangladesh\"},'BE':{'m49':56,'name':\"Belgium\"},'BF':{'m49':854,'name':\"Burkina Faso\"},'BG':{'m49':100,'name':\"Bulgaria\"},'BH':{'m49':48,'name':\"Bahrain\"},'BI':{'m49':108,'name':\"Burundi\"},'BJ':{'m49':204,'name':\"Benin\"},'BL':{'m49':652,'name':\"Saint-Barthélemy\"},'BM':{'m49':60,'name':\"Bermuda\"},'BN':{'m49':96,'name':\"Brunei Darussalam\"},'BO':{'m49':68,'name':\"Bolivia (Plurinational State of)\"},'BQ':{'m49':535,'name':\"Bonaire, Saint Eustatius and Saba\"},'BR':{'m49':76,'name':\"Brazil\"},'BS':{'m49':44,'name':\"Bahamas\"},'BT':{'m49':64,'name':\"Bhutan\"},'BW':{'m49':72,'name':\"Botswana\"},'BY':{'m49':112,'name':\"Belarus\"},'BZ':{'m49':84,'name':\"Belize\"},'CA':{'m49':124,'name':\"Canada\"},'CD':{'m49':180,'name':\"Democratic Republic of the Congo\"},'CF':{'m49':140,'name':\"Central African Republic\"},'CG':{'m49':178,'name':\"Congo\"},'CH':{'m49':756,'name':\"Switzerland\"},'CI':{'m49':384,'name':\"Côte d'Ivoire\"},'CK':{'m49':184,'name':\"Cook Islands\"},'CL':{'m49':152,'name':\"Chile\"},'CM':{'m49':120,'name':\"Cameroon\"},'CN':{'m49':156,'name':\"China\"},'CO':{'m49':170,'name':\"Colombia\"},'CR':{'m49':188,'name':\"Costa Rica\"},'CU':{'m49':192,'name':\"Cuba\"},'CV':{'m49':132,'name':\"Cabo Verde\"},'CW':{'m49':531,'name':\"Curaçao\"},'CY':{'m49':196,'name':\"Cyprus\"},'CZ':{'m49':203,'name':\"Czech Republic\"},'DE':{'m49':276,'name':\"Germany\"},'DJ':{'m49':262,'name':\"Djibouti\"},'DK':{'m49':208,'name':\"Denmark\"},'DM':{'m49':212,'name':\"Dominica\"},'DO':{'m49':214,'name':\"Dominican Republic\"},'DZ':{'m49':12,'name':\"Algeria\"},'EC':{'m49':218,'name':\"Ecuador\"},'EE':{'m49':233,'name':\"Estonia\"},'EG':{'m49':818,'name':\"Egypt\"},'EH':{'m49':732,'name':\"Western Sahara\"},'ER':{'m49':232,'name':\"Eritrea\"},'ES':{'m49':724,'name':\"Spain\"},'ET':{'m49':231,'name':\"Ethiopia\"},'FI':{'m49':246,'name':\"Finland\"},'FJ':{'m49':242,'name':\"Fiji\"},'FK':{'m49':238,'name':\"Falkland Islands (Malvinas)\"},'FM':{'m49':583,'name':\"Micronesia (Federated States of)\"},'FO':{'m49':234,'name':\"Faeroe Islands\"},'FR':{'m49':250,'name':\"France\"},'GA':{'m49':266,'name':\"Gabon\"},'GB':{'m49':826,'name':\"United Kingdom of Great Britain and Northern Ireland\"},'GD':{'m49':308,'name':\"Grenada\"},'GE':{'m49':268,'name':\"Georgia\"},'GF':{'m49':254,'name':\"French Guiana\"},'GG':{'m49':831,'name':\"Guernsey\"},'GH':{'m49':288,'name':\"Ghana\"},'GI':{'m49':292,'name':\"Gibraltar\"},'GL':{'m49':304,'name':\"Greenland\"},'GM':{'m49':270,'name':\"Gambia\"},'GN':{'m49':324,'name':\"Guinea\"},'GP':{'m49':312,'name':\"Guadeloupe\"},'GQ':{'m49':226,'name':\"Equatorial Guinea\"},'GR':{'m49':300,'name':\"Greece\"},'GT':{'m49':320,'name':\"Guatemala\"},'GU':{'m49':316,'name':\"Guam\"},'GW':{'m49':624,'name':\"Guinea-Bissau\"},'GY':{'m49':328,'name':\"Guyana\"},'HK':{'m49':344,'name':\"China, Hong Kong Special Administrative Region\"},'HN':{'m49':340,'name':\"Honduras\"},'HR':{'m49':191,'name':\"Croatia\"},'HT':{'m49':332,'name':\"Haiti\"},'HU':{'m49':348,'name':\"Hungary\"},'ID':{'m49':360,'name':\"Indonesia\"},'IE':{'m49':372,'name':\"Ireland\"},'IL':{'m49':376,'name':\"Israel\"},'IM':{'m49':833,'name':\"Isle of Man\"},'IN':{'m49':356,'name':\"India\"},'IQ':{'m49':368,'name':\"Iraq\"},'IR':{'m49':364,'name':\"Iran (Islamic Republic of)\"},'IS':{'m49':352,'name':\"Iceland\"},'IT':{'m49':380,'name':\"Italy\"},'JE':{'m49':832,'name':\"Jersey\"},'JM':{'m49':388,'name':\"Jamaica\"},'JO':{'m49':400,'name':\"Jordan\"},'JP':{'m49':392,'name':\"Japan\"},'KE':{'m49':404,'name':\"Kenya\"},'KG':{'m49':417,'name':\"Kyrgyzstan\"},'KH':{'m49':116,'name':\"Cambodia\"},'KI':{'m49':296,'name':\"Kiribati\"},'KM':{'m49':174,'name':\"Comoros\"},'KN':{'m49':659,'name':\"Saint Kitts and Nevis\"},'KP':{'m49':408,'name':\"Democratic People's Republic of Korea\"},'KR':{'m49':410,'name':\"Republic of Korea\"},'KW':{'m49':414,'name':\"Kuwait\"},'KY':{'m49':136,'name':\"Cayman Islands\"},'KZ':{'m49':398,'name':\"Kazakhstan\"},'LA':{'m49':418,'name':\"Lao People's Democratic Republic\"},'LB':{'m49':422,'name':\"Lebanon\"},'LC':{'m49':662,'name':\"Saint Lucia\"},'LI':{'m49':438,'name':\"Liechtenstein\"},'LK':{'m49':144,'name':\"Sri Lanka\"},'LR':{'m49':430,'name':\"Liberia\"},'LS':{'m49':426,'name':\"Lesotho\"},'LT':{'m49':440,'name':\"Lithuania\"},'LU':{'m49':442,'name':\"Luxembourg\"},'LV':{'m49':428,'name':\"Latvia\"},'LY':{'m49':434,'name':\"Libya\"},'MA':{'m49':504,'name':\"Morocco\"},'MC':{'m49':492,'name':\"Monaco\"},'MD':{'m49':498,'name':\"Republic of Moldova\"},'ME':{'m49':499,'name':\"Montenegro\"},'MF':{'m49':663,'name':\"Saint-Martin (French part)\"},'MG':{'m49':450,'name':\"Madagascar\"},'MH':{'m49':584,'name':\"Marshall Islands\"},'MK':{'m49':807,'name':\"The former Yugoslav Republic of Macedonia\"},'ML':{'m49':466,'name':\"Mali\"},'MM':{'m49':104,'name':\"Myanmar\"},'MN':{'m49':496,'name':\"Mongolia\"},'MO':{'m49':446,'name':\"China, Macao Special Administrative Region\"},'MP':{'m49':580,'name':\"Northern Mariana Islands\"},'MQ':{'m49':474,'name':\"Martinique\"},'MR':{'m49':478,'name':\"Mauritania\"},'MS':{'m49':500,'name':\"Montserrat\"},'MT':{'m49':470,'name':\"Malta\"},'MU':{'m49':480,'name':\"Mauritius\"},'MV':{'m49':462,'name':\"Maldives\"},'MW':{'m49':454,'name':\"Malawi\"},'MX':{'m49':484,'name':\"Mexico\"},'MY':{'m49':458,'name':\"Malaysia\"},'MZ':{'m49':508,'name':\"Mozambique\"},'NA':{'m49':516,'name':\"Namibia\"},'NC':{'m49':540,'name':\"New Caledonia\"},'NE':{'m49':562,'name':\"Niger\"},'NF':{'m49':574,'name':\"Norfolk Island\"},'NG':{'m49':566,'name':\"Nigeria\"},'NI':{'m49':558,'name':\"Nicaragua\"},'NL':{'m49':528,'name':\"Netherlands\"},'NO':{'m49':578,'name':\"Norway\"},'NP':{'m49':524,'name':\"Nepal\"},'NR':{'m49':520,'name':\"Nauru\"},'NU':{'m49':570,'name':\"Niue\"},'NZ':{'m49':554,'name':\"New Zealand\"},'OM':{'m49':512,'name':\"Oman\"},'PA':{'m49':591,'name':\"Panama\"},'PE':{'m49':604,'name':\"Peru\"},'PF':{'m49':258,'name':\"French Polynesia\"},'PG':{'m49':598,'name':\"Papua New Guinea\"},'PH':{'m49':608,'name':\"Philippines\"},'PK':{'m49':586,'name':\"Pakistan\"},'PL':{'m49':616,'name':\"Poland\"},'PM':{'m49':666,'name':\"Saint Pierre and Miquelon\"},'PN':{'m49':612,'name':\"Pitcairn\"},'PR':{'m49':630,'name':\"Puerto Rico\"},'PS':{'m49':275,'name':\"State of Palestine\"},'PT':{'m49':620,'name':\"Portugal\"},'PW':{'m49':585,'name':\"Palau\"},'PY':{'m49':600,'name':\"Paraguay\"},'QA':{'m49':634,'name':\"Qatar\"},'RE':{'m49':638,'name':\"Réunion\"},'RO':{'m49':642,'name':\"Romania\"},'RS':{'m49':688,'name':\"Serbia\"},'RU':{'m49':643,'name':\"Russian Federation\"},'RW':{'m49':646,'name':\"Rwanda\"},'SA':{'m49':682,'name':\"Saudi Arabia\"},'SB':{'m49':90,'name':\"Solomon Islands\"},'SC':{'m49':690,'name':\"Seychelles\"},'SD':{'m49':729,'name':\"Sudan\"},'SE':{'m49':752,'name':\"Sweden\"},'SG':{'m49':702,'name':\"Singapore\"},'SH':{'m49':654,'name':\"Saint Helena\"},'SI':{'m49':705,'name':\"Slovenia\"},'SJ':{'m49':744,'name':\"Svalbard and Jan Mayen Islands\"},'SK':{'m49':703,'name':\"Slovakia\"},'SL':{'m49':694,'name':\"Sierra Leone\"},'SM':{'m49':674,'name':\"San Marino\"},'SN':{'m49':686,'name':\"Senegal\"},'SO':{'m49':706,'name':\"Somalia\"},'SR':{'m49':740,'name':\"Suriname\"},'SS':{'m49':728,'name':\"South Sudan\"},'ST':{'m49':678,'name':\"Sao Tome and Principe\"},'SV':{'m49':222,'name':\"El Salvador\"},'SX':{'m49':534,'name':\"Sint Maarten (Dutch part)\"},'SY':{'m49':760,'name':\"Syrian Arab Republic\"},'SZ':{'m49':748,'name':\"Swaziland\"},'TC':{'m49':796,'name':\"Turks and Caicos Islands\"},'TD':{'m49':148,'name':\"Chad\"},'TG':{'m49':768,'name':\"Togo\"},'TH':{'m49':764,'name':\"Thailand\"},'TJ':{'m49':762,'name':\"Tajikistan\"},'TK':{'m49':772,'name':\"Tokelau\"},'TL':{'m49':626,'name':\"Timor-Leste\"},'TM':{'m49':795,'name':\"Turkmenistan\"},'TN':{'m49':788,'name':\"Tunisia\"},'TO':{'m49':776,'name':\"Tonga\"},'TR':{'m49':792,'name':\"Turkey\"},'TT':{'m49':780,'name':\"Trinidad and Tobago\"},'TV':{'m49':798,'name':\"Tuvalu\"},'TW':{'m49':158,'name':\"China, Taiwan Province of China\"},'TZ':{'m49':834,'name':\"United Republic of Tanzania\"},'UA':{'m49':804,'name':\"Ukraine\"},'UG':{'m49':800,'name':\"Uganda\"},'US':{'m49':840,'name':\"United States of America\"},'UY':{'m49':858,'name':\"Uruguay\"},'UZ':{'m49':860,'name':\"Uzbekistan\"},'VA':{'m49':336,'name':\"Holy See\"},'VC':{'m49':670,'name':\"Saint Vincent and the Grenadines\"},'VE':{'m49':862,'name':\"Venezuela (Bolivarian Republic of)\"},'VG':{'m49':92,'name':\"British Virgin Islands\"},'VI':{'m49':850,'name':\"United States Virgin Islands\"},'VN':{'m49':704,'name':\"Viet Nam\"},'VU':{'m49':548,'name':\"Vanuatu\"},'WF':{'m49':876,'name':\"Wallis and Futuna Islands\"},'WS':{'m49':882,'name':\"Samoa\"},'YE':{'m49':887,'name':\"Yemen\"},'YT':{'m49':175,'name':\"Mayotte\"},'ZA':{'m49':710,'name':\"South Africa\"},'ZM':{'m49':894,'name':\"Zambia\"},'ZW':{'m49':716,'name':\"Zimbabwe\"}}";


        private static String jsonIso3Iso2 = "{'AFG':'AF','ALB':'AL','ATA':'AQ','DZA':'DZ','ASM':'AS','AND':'AD','AGO':'AO','ATG':'AG','AZE':'AZ','ARG':'AR','AUS':'AU','AUT':'AT','BHS':'BS','BHR':'BH','BGD':'BD','ARM':'AM','BRB':'BB','BEL':'BE','BMU':'BM','BTN':'BT','BOL':'BO','BIH':'BA','BWA':'BW','BVT':'BV','BRA':'BR','BLZ':'BZ','IOT':'IO','SLB':'SB','VGB':'VG','BRN':'BN','BGR':'BG','MMR':'MM','BDI':'BI','BLR':'BY','KHM':'KH','CMR':'CM','CAN':'CA','CPV':'CV','CYM':'KY','CAF':'CF','LKA':'LK','TCD':'TD','CHL':'CL','CHN':'CN','CXR':'CX','CCK':'CC','COL':'CO','COM':'KM','MYT':'YT','COG':'CG','COD':'CD','COK':'CK','CRI':'CR','HRV':'HR','CUB':'CU','CYP':'CY','CZE':'CZ','BEN':'BJ','DNK':'DK','DMA':'DM','DOM':'DO','ECU':'EC','SLV':'SV','GNQ':'GQ','ETH':'ET','ERI':'ER','EST':'EE','FRO':'FO','FLK':'FK','SGS':'GS','FJI':'FJ','FIN':'FI','ALA':'AX','FRA':'FR','GUF':'GF','PYF':'PF','ATF':'TF','DJI':'DJ','GAB':'GA','GEO':'GE','GMB':'GM','PSE':'PS','DEU':'DE','GHA':'GH','GIB':'GI','KIR':'KI','GRC':'GR','GRL':'GL','GRD':'GD','GLP':'GP','GUM':'GU','GTM':'GT','GIN':'GN','GUY':'GY','HTI':'HT','HMD':'HM','VAT':'VA','HND':'HN','HKG':'HK','HUN':'HU','ISL':'IS','IND':'IN','IDN':'ID','IRN':'IR','IRQ':'IQ','IRL':'IE','ISR':'IL','ITA':'IT','CIV':'CI','JAM':'JM','JPN':'JP','KAZ':'KZ','JOR':'JO','KEN':'KE','PRK':'KP','KOR':'KR','KWT':'KW','KGZ':'KG','LAO':'LA','LBN':'LB','LSO':'LS','LVA':'LV','LBR':'LR','LBY':'LY','LIE':'LI','LTU':'LT','LUX':'LU','MAC':'MO','MDG':'MG','MWI':'MW','MYS':'MY','MDV':'MV','MLI':'ML','MLT':'MT','MTQ':'MQ','MRT':'MR','MUS':'MU','MEX':'MX','MCO':'MC','MNG':'MN','MDA':'MD','MNE':'ME','MSR':'MS','MAR':'MA','MOZ':'MZ','OMN':'OM','NAM':'NA','NRU':'NR','NPL':'NP','NLD':'NL','CUW':'CW','ABW':'AW','SXM':'SX','BES':'BQ','NCL':'NC','VUT':'VU','NZL':'NZ','NIC':'NI','NER':'NE','NGA':'NG','NIU':'NU','NFK':'NF','NOR':'NO','MNP':'MP','UMI':'UM','FSM':'FM','MHL':'MH','PLW':'PW','PAK':'PK','PAN':'PA','PNG':'PG','PRY':'PY','PER':'PE','PHL':'PH','PCN':'PN','POL':'PL','PRT':'PT','GNB':'GW','TLS':'TL','PRI':'PR','QAT':'QA','REU':'RE','ROU':'RO','RUS':'RU','RWA':'RW','BLM':'BL','SHN':'SH','KNA':'KN','AIA':'AI','LCA':'LC','MAF':'MF','SPM':'PM','VCT':'VC','SMR':'SM','STP':'ST','SAU':'SA','SEN':'SN','SRB':'RS','SYC':'SC','SLE':'SL','SGP':'SG','SVK':'SK','VNM':'VN','SVN':'SI','SOM':'SO','ZAF':'ZA','ZWE':'ZW','ESP':'ES','SSD':'SS','SDN':'SD','ESH':'EH','SUR':'SR','SJM':'SJ','SWZ':'SZ','SWE':'SE','CHE':'CH','SYR':'SY','TJK':'TJ','THA':'TH','TGO':'TG','TKL':'TK','TON':'TO','TTO':'TT','ARE':'AE','TUN':'TN','TUR':'TR','TKM':'TM','TCA':'TC','TUV':'TV','UGA':'UG','UKR':'UA','MKD':'MK','EGY':'EG','GBR':'GB','GGY':'GG','JEY':'JE','IMN':'IM','TZA':'TZ','USA':'US','VIR':'VI','BFA':'BF','URY':'UY','UZB':'UZ','VEN':'VE','WLF':'WF','WSM':'WS','YEM':'YE','ZMB':'ZM'}";

    //private static String s1 = jsonCountryStr.Replace(@"\","");

        private static JObject countryJson1 = JObject.Parse(jsonCountryStr.Replace(@"\", ""));
        private static JObject countryJsonISO = JObject.Parse(jsonIso3Iso2);

    }


    


}