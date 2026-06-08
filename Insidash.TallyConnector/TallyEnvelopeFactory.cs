using System;

namespace Insidash.TallyConnector
{
    public static class TallyEnvelopeFactory
    {
        public static string Build(string dataType)
        {
            if (string.Equals(dataType, "Ledger", StringComparison.OrdinalIgnoreCase))
            {
                return @"<ENVELOPE>
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
      </STATICVARIABLES>
    </DESC>
  </BODY>
</ENVELOPE>";
            }
            else if (string.Equals(dataType, "Voucher", StringComparison.OrdinalIgnoreCase))
            {
                // Sync vouchers starting from 2000-01-01 to capture full daybook history
                string fromStr = "20000101";
                string toStr   = DateTime.Now.AddDays(1).ToString("yyyyMMdd");

                return $@"<ENVELOPE>
  <HEADER>
    <VERSION>1</VERSION>
    <TALLYREQUEST>Export</TALLYREQUEST>
    <TYPE>Data</TYPE>
    <ID>Day Book</ID>
  </HEADER>
  <BODY>
    <DESC>
      <STATICVARIABLES>
        <SVEXPORTFORMAT>$$SysName:XML</SVEXPORTFORMAT>
        <SVFROMDATE>{fromStr}</SVFROMDATE>
        <SVTODATE>{toStr}</SVTODATE>
      </STATICVARIABLES>
    </DESC>
  </BODY>
</ENVELOPE>";
            }
            else if (string.Equals(dataType, "StockItem", StringComparison.OrdinalIgnoreCase))
                            {
                              return @"<ENVELOPE>
                              <HEADER>
                              <VERSION>1</VERSION>
                              <TALLYREQUEST>Export</TALLYREQUEST>
                              <TYPE>Collection</TYPE>
                              <ID>StockItem</ID>
                              </HEADER>
                              <BODY>
                              <DESC>
                                <STATICVARIABLES>
                                  <SVEXPORTFORMAT>$$SysName:XML</SVEXPORTFORMAT>
                                </STATICVARIABLES>

                                <FETCHLIST>
                                  <FETCH>NAME</FETCH>
                                  <FETCH>PARENT</FETCH>
                                  <FETCH>BASEUNITS</FETCH>
                                  <FETCH>CLOSINGBALANCE</FETCH>
                                  <FETCH>CLOSINGVALUE</FETCH>
                                </FETCHLIST>

                              </DESC>
                              </BODY>
                              </ENVELOPE>";
                            }
            else if (string.Equals(dataType, "BillOutstanding", StringComparison.OrdinalIgnoreCase))
            {
                return @"<ENVELOPE>
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
    }
}
