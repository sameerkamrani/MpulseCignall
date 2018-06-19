using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.OleDb;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using CryptographyUnit;
using System.Security.Permissions;

namespace SendTransactions
{
    public class Program
    {

        
        
        
        //


        #region globl vriables and constructor
        static int storeId;
        static string connectionAccess;
        static string connectionMySqlconfig;
        static string connectionMySql;
        static string AccessDbPath;
        static string transactionPath;
        static string topRec;
        static Program()
        {
            ExeConfigurationFileMap configMap = new ExeConfigurationFileMap();
            configMap.ExeConfigFilename = ConfigurationManager.AppSettings["PathForSettings"];
            Configuration config = ConfigurationManager.OpenMappedExeConfiguration(configMap, ConfigurationUserLevel.None);

            connectionAccess = config.ConnectionStrings.ConnectionStrings["MsAccessCS"].ConnectionString;
            connectionMySqlconfig = config.ConnectionStrings.ConnectionStrings["MySQLCS"].ConnectionString;
            storeId = Convert.ToInt32(config.AppSettings.Settings["StoreId"].Value);
            AccessDbPath = config.AppSettings.Settings["AccessDbPath"].Value;
            transactionPath = config.AppSettings.Settings["PathForTransaction"].Value;
            topRec = config.AppSettings.Settings["TopForSelectTransactions"].Value;            
            connectionMySql = AesManagedCryptography.Decrypt(connectionMySqlconfig, "kamranisaagar");
            //ConfigurationManager.RefreshSection("appSettings");
            //storeId = Convert.ToInt32(ConfigurationManager.AppSettings["StoreId"]);
            //connectionAccess = ConfigurationManager.ConnectionStrings["MsAccessCS"].ConnectionString;
            //connectionMySqlconfig = ConfigurationManager.ConnectionStrings["MySQLCS"].ConnectionString;
            //AccessDbPath = transactionPath = ConfigurationManager.AppSettings["AccessDbPath"];
            //transactionPath = ConfigurationManager.AppSettings["PathForTransaction"];
            //topRec = ConfigurationManager.AppSettings["TopForSelectTransactions"];
        }

        static int _companyId;
        static MySqlDataAdapter daSql;
        static OleDbDataAdapter daOle;
        static DataSet ds;
        static DataTable dtCatalogFromMySql;
        static DataTable dtDataForTransactions;
        static DataTable dtDataForTransline;
        static DataTable tdBrec;
        static string existingTransactionBrec = "";
        static string current = "";

        #endregion globl vriables and constructor

        #region Main Method, entry point
        static void Main(string[] args)
        {
            
            MySqlTransaction tr = null;
            
            //Getting company from storeid
            _companyId = GetStoreCompanyId(storeId);

           
            if(_companyId > 0)
            {
                //get product catalog from MyQsl
                dtCatalogFromMySql = GetCompanyCatalog(_companyId);

                //Getting existing brec no save locally in file.
                existingTransactionBrec = GetExistingBrecNos(transactionPath);

                tdBrec = new DataTable();
                tdBrec.Columns.Add("Brec_No", typeof(Int32));
                string[] brecs = existingTransactionBrec.Split(',');
                for (int i = 0; i < brecs.Length; i++)
                    tdBrec.Rows.Add(new object[] { brecs[i] });


                //collecting transation from MS Access which are not in locally brec nos
                dtDataForTransactions = CollectTransactions();
                if(dtDataForTransactions.Rows.Count == 0)
                {
                    Console.WriteLine("No record found");
                    Thread.Sleep(2300);
                    return;
                }

                //collecting transline from local on basis of already colleced transaction from MS Access       
                dtDataForTransline = CollectTransactionLine();

                //replacing barcode with product id
                //resolving duplicate records           
                ReplaceBarcodewithProductId();


                StringBuilder sCommand = new StringBuilder("INSERT IGNORE INTO transaction (transid, ticketid, storeid, TIMESTAMP, transtypeid, total) Values ");
                StringBuilder sCommand2 = new StringBuilder("INSERT IGNORE INTO transline(transid, productid, qty, price) values ");
                using (MySqlConnection con = new MySqlConnection(connectionMySql))
                {
                    con.Open();
                    tr = con.BeginTransaction();

                    List<string> Rows1 = new List<string>();
                    foreach (DataRow row in dtDataForTransactions.Rows)
                    {

                        CultureInfo MyCultureInfo = new CultureInfo("en-US");
                        string stringDate = row["TIMESTAMP1"].ToString(); // 14-Nov-2018 02:25:00 AM
                        DateTime usDate = DateTime.Parse(stringDate, MyCultureInfo, DateTimeStyles.NoCurrentDateDefault);

                        Rows1.Add(string.Format("('{0}','{1}','{2}','{3}','{4}','{5}')", MySqlHelper.EscapeString(row["transid"].ToString()),
                            MySqlHelper.EscapeString(row["ticketid"].ToString()), Convert.ToInt32(row["storeid"]),
                            usDate.ToString("yyyy-MM-dd HH:mm:ss"), Convert.ToInt32(row["transtypeid"]), Convert.ToDouble(row["total"])));
                    }
                    sCommand.Append(string.Join(",", Rows1));
                    sCommand.Append(";");
                    try
                    {
                        using (MySqlCommand myCmd = new MySqlCommand(sCommand.ToString(), con))
                        {
                            myCmd.CommandType = CommandType.Text;
                            myCmd.ExecuteNonQuery();
                        }
                    }
                    catch (Exception ex)
                    {
                        tr.Rollback();
                        Console.WriteLine(ex.Message);

                    }
                    finally
                    {

                    }

                    List<string> Rows2 = new List<string>();
                    foreach (DataRow row in dtDataForTransline.Rows)
                    {

                        Rows2.Add(string.Format("('{0}','{1}','{2}','{3}')", MySqlHelper.EscapeString(row["transid"].ToString()),
                                   MySqlHelper.EscapeString(row["barcode"].ToString()), Convert.ToDouble(row["qty"]),
                                   Convert.ToDecimal(row["price"])));
                    }
                    sCommand2.Append(string.Join(",", Rows2));
                    sCommand2.Append(";");
                    try
                    {
                        using (MySqlCommand myCmd = new MySqlCommand(sCommand2.ToString(), con))
                        {
                            myCmd.CommandType = CommandType.Text;
                            myCmd.ExecuteNonQuery();
                        }
                    }
                    catch (Exception ex)
                    {
                        tr.Rollback();
                        Console.WriteLine(ex.Message);

                    }
                    finally
                    {
                        con.Clone();
                    }
                    Console.Write("Inserting...");
                    tr.Commit();
                    InsertBrecNoTran(dtDataForTransactions, transactionPath);
                    Console.WriteLine();                   
                    Console.WriteLine();
                    Console.WriteLine("Records saved sucessfully.");
                    Thread.Sleep(2300);
                }
            }
            else
            {
                Console.WriteLine("No Company Found OR no response from server. Please try again.");
                Thread.Sleep(10000);
            }

        }
        #endregion Main Method

        #region Send Transaction Methods
        static int GetStoreCompanyId(int storeId)
        {
            int companyId = 0;
            using (MySqlConnection con = new MySqlConnection(connectionMySql))
            {
                string query = "select companyid, storename from store where storeid=" + storeId;
                MySqlCommand cmd = new MySqlCommand(query, con);
                try
                {
                    con.Open();
                    daSql = new MySqlDataAdapter();
                    daSql.SelectCommand = cmd;
                    ds = new DataSet();
                    daSql.Fill(ds, "Company");
                    companyId = Convert.ToInt32(ds.Tables[0].Rows[0]["companyid"]);
                    Console.WriteLine("Store Name: " + ds.Tables[0].Rows[0]["storename"].ToString());
                    return companyId;
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);
                    GetStoreCompanyId(storeId);
                    return companyId;
                    

                }
            }
        }
        static DataTable GetCompanyCatalog(int companyId)
        {
            DataTable dtCatalog = new DataTable();
            using (MySqlConnection con = new MySqlConnection(connectionMySql))
            {
                string query = @"SELECT barcode, productid FROM product p
                                JOIN category c ON c.categoryid = p.categoryid
                                WHERE c.companyid ='" + companyId + "' OR c.companyid IS NULL";
                MySqlCommand cmd = new MySqlCommand(query, con);
                try
                {
                    con.Open();
                    daSql = new MySqlDataAdapter();
                    daSql.SelectCommand = cmd;
                    daSql.Fill(dtCatalog);
                    if (dtCatalog.Rows.Count > 0)
                    {
                        Console.WriteLine("Total in product catalogue Records: " + dtCatalog.Rows.Count);
                    }


                    return dtCatalog;
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);
                    return null;

                }
            }
        }
        static string GetExistingBrecNos(string filePath)
        {
            string line = "";
            try
            {
                using (System.IO.TextReader tr = File.OpenText(filePath))
                {
                    line = tr.ReadLine();
                }
            }
            catch (Exception ex)
            {
                line = "0000";
            }
            return line;


        }
        static DataTable CollectTransactions()
        {
            if (topRec.Length > 0)
            {
                if(topRec.ToString() == "0")
                {
                    topRec = "";
                }
                else
                {
                    topRec = "top " + topRec;
                }
                
            }
            DataTable dtCatalog = new DataTable();
            using (OleDbConnection con = new OleDbConnection(connectionAccess))
            {

                string query = @"select " + topRec + @" '" + storeId + @"'&'-'&brec_no as transid, brec_no as ticketid, '" + storeId + @"' as storeid, ttime as TIMESTAMP1, 1 as transtypeid, round(total,2) as total
                                from receipts where [brec_no] not in (" + existingTransactionBrec + ")";
                //string query = @"select " + topRec + @" '" + storeId + @"'&'-'&brec_no as transid, brec_no as ticketid, '" + storeId + @"' as storeid, ttime as TIMESTAMP1, 1 as transtypeid, round(total,2) as total
                //               from receipts";
                OleDbCommand cmd = new OleDbCommand(query, con);
                try
                {
                    con.Open();
                    daOle = new OleDbDataAdapter();
                    daOle.SelectCommand = cmd;
                    daOle.Fill(dtCatalog);
                    if (dtCatalog.Rows.Count > 0)
                    {
                        if (current == "0000")
                        {
                            current = CurrentBrecs(dtCatalog);
                        }
                        else
                        {
                            current = CurrentBrecs(dtCatalog);
                        }

                      
                       

                        
                      
                        //var allUploaded = tdBrec.Select().Select(p => (int)p["Brec_No"]);
                        //var ToUpload = dtCatalog.Select().Where(o => !allUploaded.Contains((int)o["ticketid"])).CopyToDataTable();

                        Console.WriteLine(dtCatalog.Rows.Count + " New Records Found in Transactions");
                    }
                    return dtCatalog;
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);
                    return null;
                }
            }


        }
        static DataTable CollectTransactionLine()
        {
            DataTable dtCatalog = new DataTable();
            using (OleDbConnection con = new OleDbConnection(connectionAccess))
            {

                string query = @"select '" + storeId + @"'&'-'&r.brec_no as transid, t.prod_no as barcode, t.qty_sold as qty, t.new_uprice as price
                from 
                (trans t inner join receipts r on t.brec_no = r.brec_no) 
                where t.brec_no in (" + current + ")";
                OleDbCommand cmd = new OleDbCommand(query, con);
                try
                {
                    con.Open();
                    daOle = new OleDbDataAdapter();
                    daOle.SelectCommand = cmd;
                    daOle.Fill(dtCatalog);
                    if (dtCatalog.Rows.Count > 0)
                    {
                        Console.WriteLine(dtCatalog.Rows.Count + " New Record found in Transline with some exact duplicate");
                    }


                    return dtCatalog;


                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);
                    return null;
                }
            }


        }
        static string CurrentBrecs(DataTable brecNos)
        {
            string currentBrecs = "0000";
            StringBuilder sb = new StringBuilder();
            if (brecNos.Rows.Count > 0)
            {
                foreach (DataRow row in brecNos.Rows)
                {
                    string brec = row["transid"].ToString();
                    string[] brec_no = brec.Split('-');
                    if (sb.Length == 0)
                    {
                        sb.Append(brec_no[1]);
                    }
                    else
                    {
                        sb.Append(", " + brec_no[1]);
                    }

                }
                currentBrecs = sb.ToString();
            }

            return currentBrecs;
        }
        static bool InsertBrecNoTran(DataTable brecNos, string path)
        {
            bool flag = true;
            using (OleDbConnection con = new OleDbConnection(connectionAccess))
            {
                foreach (DataRow row in brecNos.Rows)
                {
                    string brec = row["transid"].ToString();
                    string[] brec_no = brec.Split('-');


                    if (!File.Exists(path))
                    {
                        File.Create(path).Dispose();
                        using (TextWriter tw = new StreamWriter(path))
                        {

                            tw.Write(brec_no[1]);
                            tw.Close();
                            flag = true;
                        }

                    }

                    else if (File.Exists(path))
                    {
                        using (TextWriter tw = File.AppendText(path))
                        {
                            //File.AppendText(path);
                            tw.Write(", " + brec_no[1]);
                            tw.Close();
                            flag = true;
                        }
                    }

                }
                return flag;
            }
        }
        static void ReplaceBarcodewithProductId()
        {
            //merge duplicate rows as one.
            int beforemergecount = dtDataForTransline.Rows.Count;

            dtDataForTransline = dtDataForTransline.AsEnumerable()
           .GroupBy(r => new { Col1 = r["transid"], Col2 = r["barcode"], Col3 = r["qty"], Col4 = r["price"] })
           .Select(g =>
           {
               var row1 = dtDataForTransline.NewRow();

               row1["transid"] = g.Key.Col1;
               row1["barcode"] = g.Key.Col2;
               row1["qty"] = g.Sum(r => r.Field<double>("qty"));
               row1["price"] = g.Key.Col4;

               return row1;
           })
           .CopyToDataTable();

            int aftermergecount = dtDataForTransline.Rows.Count;
            Console.WriteLine(dtDataForTransline.Rows.Count + " New Record found in Transline after merging duplicates");
            //replace barcode with productid and if not macted add Not Found
            var k = (from d in dtDataForTransline.AsEnumerable()
                     join
                     k1 in dtCatalogFromMySql.AsEnumerable() on d["barcode"] equals k1["barcode"] into dk1
                     from subset in dk1.DefaultIfEmpty()
                     select new { d, subset }).ToList();

            foreach (var m in k)
            {
                if (m.subset != null)
                {
                    if (string.Equals(m.d["barcode"], m.subset["barcode"]))
                    {
                        m.d.SetField("barcode", m.subset["productid"]);
                    }
                }
                else
                {
                    m.d.SetField("barcode", "NotFound - " + m.d["barcode"]);
                }
            }

        }
        #endregion Send Transaction Methods

       
        
    }
}
