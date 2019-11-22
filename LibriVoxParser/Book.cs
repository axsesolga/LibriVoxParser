using HtmlAgilityPack;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Serialization;

namespace LibriVoxParser
{
    //Fields (9 items)
    [Serializable()]
    public partial class Book
    {
        [System.Xml.Serialization.XmlElement("title")]
        public string title { get; set; }

        [System.Xml.Serialization.XmlElement("description")]
        public string description { get; set; }

        [System.Xml.Serialization.XmlElement("language")]
        public string language { get; set; }
        public string short_lang
        {
            get
            {
                switch (language)
                {
                    //TODO: add other lang nased on wiki short names 
                    case "Russian":
                        return "ru";
                    default:
                        return "en";
                }

            }
        }

        [System.Xml.Serialization.XmlElement("url_zip_file")]
        public string url_zip_file { get; set; }

        [System.Xml.Serialization.XmlElement("url_librivox")]
        public string url_librivox { get; set; }

        [System.Xml.Serialization.XmlElement("url_iarchive")]
        public string url_iarchive { get; set; }

        [System.Xml.Serialization.XmlElement("url_other")]
        public string url_other { get; set; }

        [System.Xml.Serialization.XmlElement("id")]
        public int id { get; set; }

        [System.Xml.Serialization.XmlElement("totaltimesecs")]
        public int totaltimesecs { get; set; }

    }

    //Filds that are ignored while writing to DB
    public partial class Book
    {
        /*trash*/
        [System.Xml.Serialization.XmlElement("url_text_source")]
        public string url_text_source { get; set; }

        [System.Xml.Serialization.XmlElement("copyright_year")]
        public string copyright_year { get; set; }

        [System.Xml.Serialization.XmlElement("num_sections")]
        public string num_sections { get; set; }

        [System.Xml.Serialization.XmlElement("url_rss")]
        public string url_rss { get; set; }

        [System.Xml.Serialization.XmlElement("url_project")]
        public string url_project { get; set; }

        [System.Xml.Serialization.XmlElement("totaltime")]
        public string totaltime { get; set; }

        /*trash that are arrays*/
        [System.Xml.Serialization.XmlElement("sections")]
        public XmlElement sections { get; set; }

        [System.Xml.Serialization.XmlElement("genres")]
        public XmlElement genres { get; set; }

        [System.Xml.Serialization.XmlElement("authors")]
        public XmlElement authors { get; set; }

        [System.Xml.Serialization.XmlElement("translators")]
        public XmlElement translators { get; set; }
        /*trash*/
    }

    // XML from URL Parse part
    public partial class Book
    {
        public static readonly ConcurrentBag<Book> libriDB = new ConcurrentBag<Book>(); // Thread-safe bag of all created books (might be repeats, and null-books

        static System.Threading.Semaphore S_read; // For control of threads count
        public static List<Task> booksTasksParsing = new List<Task>(); // For counting nubmer of link parsed

        public Book() { }
        /// <summary>
        /// Runs parsing book from url in thread (Task)
        /// </summary>
        /// <param name="url"></param>
        public Book(string url)
        {
            Task myTask = Task.Factory.StartNew(() => book_parse(url));
            booksTasksParsing.Add(myTask);
        }
        /// <summary>
        /// Makes "this" object copy of given Book "from"
        /// </summary>
        /// <param name="from"></param>
        public void deepCopy(Book from)
        {
            this.id = from.id;
            this.title = from.title;
            this.description = from.description;
            this.language = from.language;
            this.url_iarchive = from.url_iarchive;
            this.url_zip_file = from.url_zip_file;
            this.url_librivox = from.url_librivox;
            this.url_other = from.url_other;
            this.totaltimesecs = from.totaltimesecs;
        }

        /// <summary>
        ///  For deserialization from page with root = books (e.g. from audiobook by id ONLY, doesnt support multi-reading from page-"limit" api)
        /// </summary>
        [Serializable()]
        [System.Xml.Serialization.XmlRoot("xml")]
        public class OneBookUrl
        {
            [XmlArray("books")]
            [XmlArrayItem("book", typeof(Book))]
            public Book[] Books { get; set; } // expected to be length = 1
        }

        static XmlSerializer serializer = new XmlSerializer(typeof(OneBookUrl)); // Thread safe!
        public static int books_parsed = 0;
        public static int books_added = 0;
        public void book_parse(object obj_url)
        {
            S_read.WaitOne();
            string url = obj_url.ToString();
            WebClient client = new WebClient(); // not thread safe

            /// Extracting data from URL and deserialize it (
            try
            {
                string data = Encoding.Default.GetString(client.DownloadData(url));
                //not great code, but works
                //expected to get array of 1 element, it wont parse more element from URL
                Stream stream = new MemoryStream(Encoding.UTF8.GetBytes(data));
                OneBookUrl books = (OneBookUrl)serializer.Deserialize(stream);
                deepCopy(books.Books[0]); // this = books[0]
                libriDB.Add(this);
                stream.Close();
            }
            catch (Exception e)
            {
                if (!e.Message.Contains("404"))
                {
                    Console.WriteLine("for " + url + "\nerror:\n" + e.ToString());
                }
            }
            S_read.Release();
            books_parsed++;

        }
    }


    // SQLite part
    public partial class Book
    {
        /// <summary>
        /// List of threads that are writing data (libri_DB to DB file)
        /// </summary>
        public static List<Task> booksTasksWritingToDB = new List<Task>();

        /// <summary>
        /// Creats sqlite table
        /// </summary>
        /// <param name="db_path"></param>
        /// <param name="delete_old">True by default -> deletes all old data</param>
        public static void create_table(string db_path, bool delete_old = true)
        {
            SQLiteConnection conn = new SQLiteConnection("Data Source=" + db_path + "; Version=3;");
            try { conn.Open(); }
            catch (SQLiteException ex) { Console.WriteLine(ex.Message); }
            if (conn.State == ConnectionState.Open)
            {
                string sql_command = "";
                if (delete_old)
                {
                    sql_command = "DROP TABLE IF EXISTS books;";
                }
                sql_command += "CREATE TABLE IF NOT EXISTS books (" +
                    "id INT UNIQUE, " +
                    "title TEXT, " +
                    "description TEXT, " +
                    "language TEXT NOT NULL, " +
                    "totaltimesecs INT,  " +
                    "url_zip_file TEXT NOT NULL, " +
                    "url_librivox TEXT, " +
                    "url_iarchive TEXT, " +
                    "url_other TEXT," +
                    "url_gutenberg TEXT," +
                    "url_wikisource TEXT)";
                SQLiteCommand cmd = new SQLiteCommand(sql_command, conn);
                try
                {
                    cmd.ExecuteNonQuery();
                }
                catch (SQLiteException ex)
                {
                    Console.WriteLine(ex.Message);
                }
            }
        }

        /// <summary>
        /// Runs thread that adds "this" Book to DB file
        /// </summary>
        /// <param name="db_path"></param>
        public void addToDB(string db_path)
        {
            Task myTask = Task.Factory.StartNew(() => addToDBTask(db_path));
            booksTasksWritingToDB.Add(myTask);
        }

        static System.Threading.Semaphore S_sql; // For controll number of threads
        public void addToDBTask(string db_path)
        {
            S_sql.WaitOne();
            SQLiteConnection conn = new SQLiteConnection("Data Source=" + db_path + "; Version=3;");
            try { conn.Open(); }
            catch (SQLiteException ex) { Console.WriteLine(ex.Message); }

            if (conn.State == ConnectionState.Open)
            {
                string sql_command = "INSERT OR IGNORE INTO Books(id, title, description, language, totaltimesecs, url_zip_file, url_librivox, url_iarchive, url_other)" +
                                                                                            " VALUES(@id, @title, @description, " +
                                                                                            "@language, @totaltimesecs, @url_zip_file, @url_librivox, " +
                                                                                            "@url_iarchive, @url_other)";
                SQLiteCommand s = new SQLiteCommand(sql_command, conn);
                s.Parameters.AddWithValue("@id", id);
                s.Parameters.AddWithValue("@title", title);
                s.Parameters.AddWithValue("@description", description);
                s.Parameters.AddWithValue("@language", language);
                s.Parameters.AddWithValue("@totaltimesecs", totaltimesecs);
                s.Parameters.AddWithValue("@url_zip_file", url_zip_file);
                s.Parameters.AddWithValue("@url_librivox", url_librivox);
                s.Parameters.AddWithValue("@url_iarchive", url_iarchive);
                s.Parameters.AddWithValue("@url_other", url_other);
                s.Prepare();
                try
                {
                    s.ExecuteNonQuery();
                }
                catch (SQLiteException ex)
                {
                    Console.WriteLine(ex.Message);
                }
            }
            S_sql.Release();
            books_added++;
        }
        public override string ToString()
        {
            return "id:\t" + id.ToString() + "\tLanguage:\t" + language;
        }
        public override int GetHashCode()
        {
            return id.GetHashCode();
        }
    }


    // Text source search from extracted data
    public partial class Book
    {

        internal static void init_semaphores(int max_thread)
        {
            S_read = new System.Threading.Semaphore(max_thread, max_thread);
            S_sql = new System.Threading.Semaphore(max_thread, max_thread);
            S_url_sql = new System.Threading.Semaphore(max_thread, max_thread);
        }
        public string url_gutenberg
        {
            get
            {
                string from = this.url_librivox;
                var doc = new HtmlWeb().Load(from);
                var linkTags = doc.DocumentNode.Descendants("link");
                var linkedPages = doc.DocumentNode.Descendants("a")
                                                  .Select(a => a.GetAttributeValue("href", null))
                                                  .Where(u => !String.IsNullOrEmpty(u))
                                                  .Where(u => u.Contains("gutenberg"));// || u.Contains(".txt"));
                if (linkedPages.Any())
                {
                    return linkedPages.First();
                }
                else
                {
                    return null;
                }

            }
            set { }
        }
        public string url_wikisource
        {
            get
            {
                string from = this.url_librivox;
                var doc = new HtmlWeb().Load(from);
                var linkTags = doc.DocumentNode.Descendants("link");
                var linkedPages = doc.DocumentNode.Descendants("a")
                                                  .Select(a => a.GetAttributeValue("href", null))
                                                  .Where(u => !String.IsNullOrEmpty(u))
                                                  .Where(u => u.Contains("wikisource"));// || u.Contains(".txt"));
                if (linkedPages.Any())
                {
                    return linkedPages.First();
                }

                string wikisource_books = @"https://tools.wmflabs.org/wsexport/tool/book.php?lang={0}&format=txt&page="; //%s -> lang, end -> title formated
                //string test_title = "Count of Monte Cristo";
                string url = string.Format(wikisource_books +
                    new string(this.title
                    .Replace("  ", " ")
                    .Replace(" ", "+")
                    .Replace("version", "")
                    .Where(c => char.IsLetter(c) || c == '+').ToArray()), this.short_lang);

                return url;

            }
            set { }
        }

        public void addTextUrl(string db_path)
        {
            Task myTask = Task.Factory.StartNew(() => addTextUrlTask(db_path));
        }

        static System.Threading.Semaphore S_url_sql; // For controll number of threads
        void addTextUrlTask(string db_path)
        {
            S_url_sql.WaitOne();

            SQLiteConnection conn = new SQLiteConnection("Data Source=" + db_path + "; Version=3;");
            try { conn.Open(); }
            catch (SQLiteException ex) { Console.WriteLine(ex.Message); }

            if (conn.State == ConnectionState.Open)
            {
                string sql_command = "UPDATE Books SET url_gutenberg = @url_gutenberg, url_wikisource = @url_wikisource WHERE id = @id";

                SQLiteCommand s = new SQLiteCommand(sql_command, conn);
                s.Parameters.AddWithValue("@url_gutenberg", url_gutenberg.ToString());
                s.Parameters.AddWithValue("@url_wikisource", url_wikisource.ToString());
                s.Parameters.AddWithValue("@id", id);
                s.Prepare();
                try
                {
                    s.ExecuteNonQuery();
                }
                catch (SQLiteException ex)
                {
                    Console.WriteLine(ex.Message);
                }
            }
            books_added++;

            S_url_sql.Release();
        }

    }
}
    