using System;

namespace Insidash.TallyConnector
{
  public static class TallyEnvelopeFactory
  {
    // Accepts tallyCompanyName to inject SVCURRENTCOMPANY targeting
    public static string Build(string dataType, string tallyCompanyName)
    {
      // Escape special characters (e.g. '&', '<', '>') if company name contains them
      string companyTag = string.IsNullOrWhiteSpace(tallyCompanyName)
          ? ""
          : $"<SVCURRENTCOMPANY>{System.Security.SecurityElement.Escape(tallyCompanyName)}</SVCURRENTCOMPANY>";

      if (string.Equals(dataType, "Ledger", StringComparison.OrdinalIgnoreCase))
      {
        return $@"<ENVELOPE>
          <HEADER>
            <VERSION>1</VERSION>
            <TALLYREQUEST>Export</TALLYREQUEST>
            <TYPE>Collection</TYPE>
            <ID>Ledger</ID>
          </HEADER>
          <BODY>
            <DESC>
              <STATICVARIABLES>
                <SVEXPORTFORMAT>$$SysName:XML</SVEXPORTFORMAT>
                {companyTag}
              </STATICVARIABLES>
            </DESC>
          </BODY>
        </ENVELOPE>";
      }
      else if (string.Equals(dataType, "Voucher", StringComparison.OrdinalIgnoreCase))
      {
        string fromStr = "20000101";
        string toStr = DateTime.Now.AddDays(1).ToString("yyyyMMdd");

        return $@"<ENVELOPE>
        <HEADER>
          <VERSION>1</VERSION>
          <TALLYREQUEST>Export</TALLYREQUEST>
          <TYPE>Collection</TYPE>
          <ID>MyVoucherCollection</ID>
        </HEADER>
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
                  <NATIVEMETHOD>DATE</NATIVEMETHOD>
                  <NATIVEMETHOD>VOUCHERTYPENAME</NATIVEMETHOD>
                  <NATIVEMETHOD>PARTYLEDGERNAME</NATIVEMETHOD>
                  <NATIVEMETHOD>AMOUNT</NATIVEMETHOD>
                  <NATIVEMETHOD>NARRATION</NATIVEMETHOD>
                  <NATIVEMETHOD>GUID</NATIVEMETHOD>
                </COLLECTION>
              </TDLMESSAGE>
            </TDL>
          </DESC>
        </BODY>
        </ENVELOPE>";
      }
      else if (string.Equals(dataType, "StockItem", StringComparison.OrdinalIgnoreCase))
      {
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
      }
      else if (string.Equals(dataType, "BillOutstanding", StringComparison.OrdinalIgnoreCase))
      {
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
                    <TYPE>Ledger</TYPE>
                    <FILTER>IsADebtors</FILTER>
                    <NATIVEMETHOD>NAME</NATIVEMETHOD>
                    <NATIVEMETHOD>CLOSINGBALANCE</NATIVEMETHOD>
                    <NATIVEMETHOD>BILLDETAILS.LIST</NATIVEMETHOD>
                  </COLLECTION>
                  <SYSTEM TYPE=""Formulae"" NAME=""IsADebtors"">
                    $$IsDebtors:$PARENT
                  </SYSTEM>
                </TDLMESSAGE>
              </TDL>
            </DESC>
          </BODY>
        </ENVELOPE>";
      }

      throw new ArgumentException($"Unsupported Tally data type: {dataType}");
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
                <COLLECTION NAME=""MyCompanyList"" ISINITIALIZE=""Yes"">
                  <TYPE>Company</TYPE>
                  <NATIVEMETHOD>NAME</NATIVEMETHOD>
                </COLLECTION>
              </TDLMESSAGE>
            </TDL>
          </DESC>
        </BODY>
      </ENVELOPE>";
    }
  }
}