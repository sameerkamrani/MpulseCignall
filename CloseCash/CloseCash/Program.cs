using CryptographyUnit;
using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.OleDb;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CloseCash
{
    class Program
    {
        #region globl vriables and constructor
        static int storeId;
        static string connectionAccess;
        static string connectionMySqlconfig;
        static string connectionMySql;
        static string AccessDbPath;
        static string CloseCashPath;
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
            CloseCashPath = config.AppSettings.Settings["PathForClosePath"].Value;
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

        static DataTable dtDataFromRecipt;
        
        static DateTime lastCloseCash;

        #endregion globl vriables and constructor
        static void Main(string[] args)
        {
            dtDataFromRecipt = CollectDataFromReciept();
            StoreIntoTempCC(dtDataFromRecipt);
        }

        static DateTime GetLastCloseCash(string filePath)
        {
            string line = "";
            DateTime time = DateTime.Now;
            try
            {
                using (System.IO.TextReader tr = File.OpenText(filePath))
                {
                    line = tr.ReadLine();
                    time = Convert.ToDateTime(line);
                }
            }
            catch (Exception ex)
            {
                return time;
            }
            return time;


        }

        static DataTable CollectDataFromReciept()
        {
            DataTable dtCatalog = new DataTable();
            lastCloseCash = GetLastCloseCash(CloseCashPath);
            using (OleDbConnection con = new OleDbConnection(connectionAccess))
            {
                con.Open();

                
                string query = @"SELECT SUM(IIf(SALE_TYPE = 'RS', total, 0)) AS rstotal,
                                SUM(IIf(SALE_TYPE = 'WS', total, 0)) AS wstotal,
                                count(brec_no) as customercount from receipts
                                where ttime > #" + lastCloseCash + "#";



                OleDbCommand cmd = new OleDbCommand(query, con);
                try
                {

                    daOle = new OleDbDataAdapter();
                    daOle.SelectCommand = cmd;
                    daOle.Fill(dtCatalog);
                    if (dtCatalog.Rows.Count > 0)
                    {
                        double amount = 0.00;
                        if (dtCatalog.Rows[0].ItemArray[0] == DBNull.Value)
                        {
                            amount = 0.00;
                        }
                        else
                        {
                            amount = double.Parse(dtCatalog.Rows[0].ItemArray[0].ToString());
                        }
                        double wsamount = 0.00;
                        if (dtCatalog.Rows[0].ItemArray[1] == DBNull.Value)
                        {
                            wsamount = 0.00;
                        }
                        else
                        {
                            wsamount = double.Parse(dtCatalog.Rows[0].ItemArray[1].ToString());
                        }

                        Console.WriteLine("Retail Sale: "+ amount.ToString() + ", Whole Sale: " + wsamount + ", Customers: " + dtCatalog.Rows[0].ItemArray[2].ToString());
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

        static int StoreIntoTempCC(DataTable dt)
        {

            using (MySqlConnection con = new MySqlConnection(connectionMySql))
            {
                double amount = 0.00;
                int custcount = Convert.ToInt32(dt.Rows[0].ItemArray[2]);
                if(custcount > 0)
                {
                    if (dt.Rows[0].ItemArray[0] == DBNull.Value)
                    {
                        amount = 0.00;
                    }
                    else
                    {
                        amount = double.Parse(dt.Rows[0].ItemArray[0].ToString());
                    }

                    double wsamount = 0.00;
                    if (dt.Rows[0].ItemArray[1] == DBNull.Value)
                    {
                        wsamount = 0.00;
                    }
                    else
                    {
                        wsamount = double.Parse(dt.Rows[0].ItemArray[1].ToString());
                    }

                    string query = "INSERT INTO tempclosecash (amount,customercount, storeid, DATE, isProcessedByPortal, wsamount) Values (@amount, @custcount, @storeId, @date, @isProcessed, @wsamount)";
                    MySqlCommand cmd = new MySqlCommand(query, con);

                    cmd.Parameters.AddWithValue("@amount", amount);
                    cmd.Parameters.AddWithValue("@custcount", custcount);
                    cmd.Parameters.AddWithValue("@storeId", storeId);
                    cmd.Parameters.AddWithValue("@date", DateTime.Now);
                    cmd.Parameters.AddWithValue("@isProcessed", 0);
                    cmd.Parameters.AddWithValue("@wsamount", wsamount);


                    try
                    {
                        con.Open();
                        daSql = new MySqlDataAdapter();
                        int result = cmd.ExecuteNonQuery();
                        if (result > 0)
                        {
                            UpdateCloseCashTime(CloseCashPath);
                            Console.WriteLine("Sucessfull");
                            Thread.Sleep(2300);
                        }
                        return result;

                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(ex.Message);
                        Thread.Sleep(2300);
                        return 0;
                    }
                }
                else
                {
                    Console.WriteLine("Nothing to update");
                    Thread.Sleep(2300);
                    return 0;
                }
                
            }
        }

        static bool UpdateCloseCashTime(string path)
        {

            if (!File.Exists(path))
            {
                File.Create(path).Dispose();
                using (TextWriter tw = new StreamWriter(path))
                {

                    tw.Write(DateTime.Now);
                    tw.Close();

                }
                return true;
            }

            else if (File.Exists(path))
            {
                using (TextWriter tw = new StreamWriter(path))
                {

                    tw.Write(DateTime.Now);
                    tw.Close();
                    return true;
                }


            }
            return true;
        }

    }
    
}
