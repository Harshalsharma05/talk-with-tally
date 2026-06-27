using System;
using System.Xml.Linq;

namespace Insidash.TallyConnector
{
    public enum TallyDataType
    {
        CompanyMetadata,
        Group,
        Ledger,
        Voucher,
        StockItem,
        BillOutstanding
    }

    public static class TallyEnvelopeFactory
    {
        // Notice endAlterId is completely gone. We only need the floor boundary.
        public static string Build(
            TallyDataType dataType,
            string tallyCompanyName,
            int startAlterId = 0,
            DateTime? fyStartDate = null)
        {
            string companyTag = string.IsNullOrWhiteSpace(tallyCompanyName)
                ? ""
                : $"<SVCURRENTCOMPANY>{EscapeXml(tallyCompanyName)}</SVCURRENTCOMPANY>";

            switch (dataType)
            {
                case TallyDataType.CompanyMetadata:
                    return $@"<ENVELOPE>
                      <HEADER><VERSION>1</VERSION><TALLYREQUEST>Export</TALLYREQUEST><TYPE>Collection</TYPE><ID>MyCompanyMetadata</ID></HEADER>
                      <BODY>
                        <DESC>
                          <STATICVARIABLES><SVEXPORTFORMAT>$$SysName:XML</SVEXPORTFORMAT>{companyTag}</STATICVARIABLES>
                          <TDL>
                            <TDLMESSAGE>
                              <COLLECTION NAME=""MyCompanyMetadata"">
                                <TYPE>Company</TYPE>
                                <NATIVEMETHOD>NAME</NATIVEMETHOD>
                                <NATIVEMETHOD>STARTINGFROM</NATIVEMETHOD>
                                <NATIVEMETHOD>GUID</NATIVEMETHOD>
                                <NATIVEMETHOD>COMPANYNUMBER</NATIVEMETHOD>
                              </COLLECTION>
                            </TDLMESSAGE>
                          </TDL>
                        </DESC>
                      </BODY>
                    </ENVELOPE>";

                case TallyDataType.Group:
                    return $@"<ENVELOPE>
                    <HEADER><VERSION>1</VERSION><TALLYREQUEST>Export</TALLYREQUEST><TYPE>Collection</TYPE><ID>MyGroupCollection</ID></HEADER>
                    <BODY>
                    <DESC>
                        <STATICVARIABLES><SVEXPORTFORMAT>$$SysName:XML</SVEXPORTFORMAT>{companyTag}</STATICVARIABLES>
                        <TDL>
                        <TDLMESSAGE>
                            <COLLECTION NAME=""MyGroupCollection"">
                            <TYPE>Group</TYPE>
                            <FETCH>AlterId</FETCH>
                            <FILTER>AlterIdChunkFilter</FILTER>
                            <NATIVEMETHOD>NAME</NATIVEMETHOD>
                            <NATIVEMETHOD>PARENT</NATIVEMETHOD>
                            <NATIVEMETHOD>ALTERID</NATIVEMETHOD>
                            </COLLECTION>
                            <SYSTEM TYPE=""Formulae"" NAME=""AlterIdChunkFilter"">
                            $AlterId &gt; {startAlterId}
                            </SYSTEM>
                        </TDLMESSAGE>
                        </TDL>
                    </DESC>
                    </BODY>
                    </ENVELOPE>";

                case TallyDataType.Ledger:
                    return $@"<ENVELOPE>
                    <HEADER><VERSION>1</VERSION><TALLYREQUEST>Export</TALLYREQUEST><TYPE>Collection</TYPE><ID>MyLedgers</ID></HEADER>
                    <BODY>
                    <DESC>
                        <STATICVARIABLES><SVEXPORTFORMAT>$$SysName:XML</SVEXPORTFORMAT>{companyTag}</STATICVARIABLES>
                        <TDL>
                        <TDLMESSAGE>
                            <COLLECTION NAME=""MyLedgers"">
                            <TYPE>Ledger</TYPE>
                            <FETCH>AlterId</FETCH>
                            <FILTER>AlterIdChunkFilter</FILTER>
                            <NATIVEMETHOD>NAME</NATIVEMETHOD>
                            <NATIVEMETHOD>PARENT</NATIVEMETHOD>
                            <NATIVEMETHOD>CLOSINGBALANCE</NATIVEMETHOD>
                            <NATIVEMETHOD>ALTERID</NATIVEMETHOD>
                            </COLLECTION>
                            <SYSTEM TYPE=""Formulae"" NAME=""AlterIdChunkFilter"">
                                $AlterId &gt; {startAlterId}
                            </SYSTEM>
                        </TDLMESSAGE>
                        </TDL>
                    </DESC>
                    </BODY>
                </ENVELOPE>";

                case TallyDataType.Voucher:
                    {
                        DateTime fromDate = fyStartDate ?? new DateTime(DateTime.Now.Year, 4, 1);
                        string fromStr = fromDate.ToString("yyyyMMdd");
                        string toStr = DateTime.Now.AddDays(1).ToString("yyyyMMdd");

                        return $@"<ENVELOPE>
                    <HEADER><VERSION>1</VERSION><TALLYREQUEST>Export</TALLYREQUEST><TYPE>Collection</TYPE><ID>MyVoucherCollection</ID></HEADER>
                    <BODY>
                      <DESC>
                        <STATICVARIABLES>
                          <SVEXPORTFORMAT>$$SysName:XML</SVEXPORTFORMAT>
                          <SVFROMDATE TYPE=""Date"">{fromStr}</SVFROMDATE>
                          <SVTODATE TYPE=""Date"">{toStr}</SVTODATE>
                          {companyTag}
                        </STATICVARIABLES>
                        <TDL>
                          <TDLMESSAGE>
                            <COLLECTION NAME=""MyVoucherCollection"">
                              <TYPE>Voucher</TYPE>
                              <FETCH>AlterId</FETCH>
                              <FILTER>AlterIdChunkFilter</FILTER>
                              <NATIVEMETHOD>DATE</NATIVEMETHOD>
                              <NATIVEMETHOD>VOUCHERTYPENAME</NATIVEMETHOD>
                              <NATIVEMETHOD>PARTYLEDGERNAME</NATIVEMETHOD>
                              <NATIVEMETHOD>AMOUNT</NATIVEMETHOD>
                              <NATIVEMETHOD>NARRATION</NATIVEMETHOD>
                              <NATIVEMETHOD>GUID</NATIVEMETHOD>
                              <NATIVEMETHOD>ALTERID</NATIVEMETHOD>
                            </COLLECTION>
                            <SYSTEM TYPE=""Formulae"" NAME=""AlterIdChunkFilter"">
                              $AlterId &gt; {startAlterId}
                            </SYSTEM>
                          </TDLMESSAGE>
                        </TDL>
                      </DESC>
                    </BODY>
                    </ENVELOPE>";
                    }

                case TallyDataType.StockItem:
                    return $@"<ENVELOPE>
                        <HEADER>
                        <VERSION>1</VERSION>
                        <TALLYREQUEST>Export</TALLYREQUEST>
                        <TYPE>Collection</TYPE>
                        <ID>MyStockItemCollection</ID>
                        </HEADER>
                        <BODY>
                        <DESC>
                            <STATICVARIABLES>
                            <SVEXPORTFORMAT>$$SysName:XML</SVEXPORTFORMAT>
                            {companyTag}
                            </STATICVARIABLES>
                            <TDL>
                            <TDLMESSAGE>
                                <COLLECTION NAME=""MyStockItemCollection"">
                                <TYPE>StockItem</TYPE>
                                <NATIVEMETHOD>NAME</NATIVEMETHOD>
                                <NATIVEMETHOD>PARENT</NATIVEMETHOD>
                                <NATIVEMETHOD>BASEUNITS</NATIVEMETHOD>
                                <NATIVEMETHOD>CLOSINGBALANCE</NATIVEMETHOD>
                                <NATIVEMETHOD>CLOSINGVALUE</NATIVEMETHOD>
                                </COLLECTION>
                            </TDLMESSAGE>
                            </TDL>
                        </DESC>
                        </BODY>
                    </ENVELOPE>";

                case TallyDataType.BillOutstanding:
                    return $@"<ENVELOPE>
                      <HEADER>
                        <VERSION>1</VERSION>
                        <TALLYREQUEST>Export</TALLYREQUEST>
                        <TYPE>Collection</TYPE>
                        <ID>MyOutstandingCollection</ID>
                      </HEADER>
                      <BODY>
                        <DESC>
                          <STATICVARIABLES>
                            <SVEXPORTFORMAT>$$SysName:XML</SVEXPORTFORMAT>
                            {companyTag}
                          </STATICVARIABLES>
                          <TDL>
                            <TDLMESSAGE>
                              <COLLECTION NAME=""MyOutstandingCollection"">
                                <TYPE>Bill</TYPE>
                                <NATIVEMETHOD>NAME</NATIVEMETHOD>
                                <NATIVEMETHOD>PARENT</NATIVEMETHOD>
                                <NATIVEMETHOD>BILLDATE</NATIVEMETHOD>
                                <NATIVEMETHOD>BILLDATEDUE</NATIVEMETHOD>
                                <NATIVEMETHOD>CLOSINGBALANCE</NATIVEMETHOD>
                              </COLLECTION>
                            </TDLMESSAGE>
                          </TDL>
                        </DESC>
                      </BODY>
                    </ENVELOPE>";

                default:
                    throw new ArgumentException($"Unsupported Tally data type: {dataType}");
            }
        }

        public static string BuildCompanyListEnvelope()
        {
            return @"<ENVELOPE>
              <HEADER>
                <VERSION>1</VERSION>
                <TALLYREQUEST>Export</TALLYREQUEST>
                <TYPE>Collection</TYPE>
                <ID>MyCompanyList</ID>
              </HEADER>
              <BODY>
                <DESC>
                  <STATICVARIABLES>
                    <SVEXPORTFORMAT>$$SysName:XML</SVEXPORTFORMAT>
                  </STATICVARIABLES>
                  <TDL>
                    <TDLMESSAGE>
                      <COLLECTION NAME=""MyCompanyList"">
                        <TYPE>Company</TYPE>
                        <FETCH>Name</FETCH>
                      </COLLECTION>
                    </TDLMESSAGE>
                  </TDL>
                </DESC>
              </BODY>
            </ENVELOPE>";
        }

        private static string EscapeXml(string value)
        {
            return new XText(value).ToString();
        }
    }
}