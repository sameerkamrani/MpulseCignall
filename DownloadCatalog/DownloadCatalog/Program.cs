using CryptographyUnit;
using MySql.Data.MySqlClient;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.OleDb;
using System.Linq;
using System.Security.Permissions;
using System.Text;
using System.Threading.Tasks;

namespace DownloadCatalog
{
    class Program
    {

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
        static DataTable dtDataForUpdate;
        static DataTable dtDataForExistingProducts;
        static string dtDataForInsert;
        //static DataSet dtDataForQuickSale;
        

        #endregion globl vriables and constructor
        static void Main(string[] args)
        {
            _companyId = GetStoreCompanyId(storeId);
            dtCatalogFromMySql = GetCompanyCatalog(_companyId);
            if (dtCatalogFromMySql.Rows.Count > 0)
            {
                dtDataForExistingProducts = CollectexistingProducts();
                if (dtDataForExistingProducts.Rows.Count > 0)
                {
                    UpdateRecordsForProducts();
                    InsertRecordsforProducts();

                    bool res = Delete();
                    InsertRecordsForQuickSale();
                }else
                {
                    Console.WriteLine("No Data Found");
                }

            }else
            {
                Console.WriteLine("No Data Found");
            }
            

            Console.WriteLine("Done");

        }

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
                string query = @"SELECT barcode, SUBSTRING(productname,1,34) AS productname, cost,
                                IFNULL(sp.saleprice,p.saleprice) AS pricesell, taxperc*100, p.ISVISIBLE , p.ISACTIVE
                                FROM product p
                                JOIN category c ON c.categoryid=p.categoryid AND c.companyid='" + _companyId + @"' 
                                JOIN taxclass t ON t.taxid=p.taxid
                                LEFT JOIN storeproduct sp ON sp.productid=p.productid AND sp.storeid='" + storeId + @"'
                                WHERE p.ISACTIVE=1";
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
                    return dtCatalog;

                }
            }
        }

        static DataTable CollectexistingProducts()
        {
            DataTable dtCatalog = new DataTable();
            using (OleDbConnection con = new OleDbConnection(connectionAccess))
            {
                string query = @"select Prod_No, Descript, Retail_PRC from products";
                OleDbCommand cmd = new OleDbCommand(query, con);
                try
                {
                    con.Open();
                    daOle = new OleDbDataAdapter();
                    daOle.SelectCommand = cmd;
                    daOle.Fill(dtCatalog);
                    if (dtCatalog.Rows.Count > 0)
                    {
                        Console.WriteLine(dtCatalog.Rows.Count + "  Exisiting Record found in Products");
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
        static void InsertRecordsforProducts()
        {
            FileIOPermission f2 = new FileIOPermission(FileIOPermissionAccess.Read, AccessDbPath);
            f2.AddPathList(FileIOPermissionAccess.Write | FileIOPermissionAccess.Read, AccessDbPath);
            using (OleDbConnection con = new OleDbConnection(connectionAccess))
            {
                List<DataRow> matchingRows = dtCatalogFromMySql.AsEnumerable().Where(a => !dtDataForExistingProducts.AsEnumerable()
                                            .Select(b => b.Field<string>("Prod_No")).Contains(a.Field<string>("barcode"))).ToList();

               
                con.Open();

                Console.Write("Inserting " + matchingRows.Count + " new records");
                foreach (DataRow row in matchingRows)
                {
                   
                        string query = @"insert into Products (Prod_no, Descript, Curr_cost, Retail_PRC, Tax_rate, Active_FLG, MIN_MARGIN, PRIOR_PROMO_COST_PRICE, PRIOR_PROMO_RETAIL_PRICE) 
                    values (@barcode, @productname, @cost, @saleprice, @taxperc, @isactive, @minmargin, @priorprmorcost, @priorpromoretail)";
                        OleDbCommand cmd = new OleDbCommand(query, con);
                        string val = row.Field<bool>("isactive").ToString();
                        string isActive = val.ToUpper() == "TRUE" ? "Y" : "N";
                    string product = row.Field<string>("productname").ToString();
                    if(product.Length >= 34) { product = product.Substring(0, 34); }
                    
                        cmd.Parameters.AddWithValue("@barcode", row.Field<string>("barcode"));
                        cmd.Parameters.AddWithValue("@productname", product);
                        cmd.Parameters.AddWithValue("@cost", row.Field<double>("cost"));
                        cmd.Parameters.AddWithValue("@saleprice", row.Field<double>("pricesell"));
                        cmd.Parameters.AddWithValue("@taxperc", row.Field<double>("taxperc*100"));
                        cmd.Parameters.AddWithValue("@isactive", isActive);
                        cmd.Parameters.AddWithValue("@minmargin", 100);
                        cmd.Parameters.AddWithValue("@priorprmorcost", row.Field<double>("cost"));
                        cmd.Parameters.AddWithValue("@priorpromoretail", row.Field<double>("pricesell"));

                    try
                        {
                            
                            daOle = new OleDbDataAdapter();
                            cmd.ExecuteNonQuery();
                        Console.Write(".");

                    }
                        catch (Exception ex)
                        {
                        Console.WriteLine();
                        Console.WriteLine(ex.Message);

                        }
                    }


                Console.WriteLine();
                
                
            }
        }

        static void UpdateRecordsForProducts()
        {
            //List<DataRow> matchingRows = (from s1 in dtCatalogFromMySql.AsEnumerable()
            //                              join s2 in dtDataForExistingProducts.AsEnumerable() on s1.Field<string>("barcode") equals s2.Field<string>("Prod_No")                                          
            //                              select s1).ToList();

            //List<DataRow> matchingRows = dtCatalogFromMySql.AsEnumerable().Where(a => dtDataForExistingProducts.AsEnumerable()
            //                                .Select(b => b.Field<string>("Prod_No")).Contains(a.Field<string>("barcode")))
            //                                .Where(a => !dtDataForExistingProducts.AsEnumerable()
            //                                .Select(b => b.Field<string>("Descript")).Contains(a.Field<string>("productname")))
            //                                .Where(a => !dtDataForExistingProducts.AsEnumerable()
            //                                .Select(b => b.Field<decimal>("Retail_PRC")).Contains(Convert.ToDecimal(a.Field<double>("pricesell"))))
            //                                .ToList();

            List<DataRow> matchingRows= (from t1 in dtCatalogFromMySql.AsEnumerable()
                              join t2 in dtDataForExistingProducts.AsEnumerable() on (string)t1["barcode"] equals (string)t2["Prod_No"]
                              where (string)t1["productname"] != (string)t2["descript"] ||
                                    Convert.ToDecimal((double)t1["pricesell"]) != (decimal)t2["Retail_PRC"]
                              select t1).ToList();

    //        dtDataForExistingProducts.AsEnumerable()
    //.Join(dtCatalogFromMySql.AsEnumerable(),
    //    ep => ep.Field<string>("Prod_No"),
    //    ce => ce.Field<string>("barcode"),
    //    (ep, ce) => new { ExistingProduct = ep, CatalogEntry = ce })
    //.Where(m => !m.ExistingProduct.Field<string>("Descript")
    //    .Equals(m.CatalogEntry.Field<string>("productname")))
    //.Where(m => decimal.Parse(m.ExistingProduct.Field<decimal>("Retail_PRC").ToString())
    //    != decimal.Parse(m.CatalogEntry.Field<double>("pricesell").ToString()))
    //.ToList();

            Console.Write("Updating "+matchingRows.Count+ " existing records");
            foreach (DataRow row in matchingRows)
            {
                using (OleDbConnection con = new OleDbConnection(connectionAccess))
                {
                    con.Open();
                    //Insert into Products(Prod_no, Descript, Curr_cost, Retail_PRC, Tax_rate, Active_FLG ? y : n)
                    string query = @"update Products set Descript = @productname, Curr_cost =@cost , Retail_PRC = @saleprice, Tax_rate = @taxperc, Active_FLG = @isactive where Prod_No = @barcode";

                    //string query = @"update Products set Descript = 'Qadeer' where Prod_No = '041689200657'";

                    OleDbCommand cmd = new OleDbCommand(query, con);
                    string val = row.Field<bool>("isactive").ToString();
                    string isActive = val.ToUpper() == "TRUE" ? "Y" : "N";
                    string barcode = row.Field<string>("barcode").ToString();
                    string product = row.Field<string>("productname").ToString();
                    if (product.Length >= 34) { product = product.Substring(0, 34); }
                    double cost = row.Field<double>("cost");
                    double saleprice = row.Field<double>("pricesell");
                    double tax_perc = row.Field<double>("taxperc*100");

                    cmd.Parameters.AddWithValue("@productname", product);                    
                    cmd.Parameters.AddWithValue("@cost", cost);
                    cmd.Parameters.AddWithValue("@saleprice", saleprice);
                    cmd.Parameters.AddWithValue("@taxperc", tax_perc);
                    cmd.Parameters.AddWithValue("@isactive", isActive);
                    cmd.Parameters.AddWithValue("@barcode", barcode);

                    try
                    {
                        
                        daOle = new OleDbDataAdapter();
                        cmd.ExecuteNonQuery();
                        Console.Write(".");

                    }
                    catch (Exception ex)
                    {
                        string msg = "";
                        if(ex.Message.Contains("Operation must use an updateable query"))
                        {
                            msg = "Run Program as Administrator";
                        }else
                        {
                            msg = ex.Message;
                        }
                        Console.WriteLine();
                        Console.WriteLine(msg);

                    }
                }
            }

            Console.WriteLine();
        }
        static bool Delete()
        {
            bool flag = false;
            using (OleDbConnection con = new OleDbConnection(connectionAccess))
            {
                string query = @"delete * from QUICK_SALE";
                OleDbCommand cmd = new OleDbCommand(query, con);

                try
                {
                    con.Open();
                    daOle = new OleDbDataAdapter();
                    int result = cmd.ExecuteNonQuery();
                    if (result > 0)
                    {
                        flag = true;
                    }
                    else
                    {
                        flag = false;

                    }

                    return flag;
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);
                    return flag;

                }
            }
        }
        static void InsertRecordsForQuickSale()
        {
            List<DataRow> matchingRows = (from s1 in dtCatalogFromMySql.AsEnumerable()
                                          where s1.Field<bool>("isvisible").Equals(true)
                                          orderby s1.Field<string>("productname")
                                          select s1).ToList();


            
            int i = 1;
            foreach (DataRow row in matchingRows)
            {
                using (OleDbConnection con = new OleDbConnection(connectionAccess))
                {
                    string query = @"Insert into QUICK_SALE (quick_sale_type_id, button_id, single_prod_no, button_desc, row_ver) VALUES (@qstId, @btnId, @barcode, @productname, @row)";

                    OleDbCommand cmd = new OleDbCommand(query, con);
                    cmd.Parameters.AddWithValue("@qstId", 1);
                    cmd.Parameters.AddWithValue("@btnId", i);
                    cmd.Parameters.AddWithValue("@barcode", row.Field<string>("barcode"));
                    cmd.Parameters.AddWithValue("@productname", row.Field<string>("productname"));
                    cmd.Parameters.AddWithValue("@row", 1);

                    

                    try
                    {
                        con.Open();
                        daOle = new OleDbDataAdapter();
                        cmd.ExecuteNonQuery();

                        i++;

                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine();
                        Console.WriteLine(ex.Message);
                    }
                }
            }
        }
        
        
    }
}
