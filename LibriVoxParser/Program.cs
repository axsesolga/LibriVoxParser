using HtmlAgilityPack;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace LibriVoxParser
{
    class Program
    {
        static public string libri_book_by_id_url = @"https://librivox.org/api/feed/audiobooks?extended=1&id=";
        static string libri_db_path = @"..\..\data\libri_vox.db";

        public static bool print_status_read = true;
        public static bool print_status_db = false;

        public static void ClearCurrentConsoleLine()
        {
            int currentLineCursor = Console.CursorTop;
            Console.SetCursorPosition(0, Console.CursorTop);
            Console.Write(new string(' ', Console.WindowWidth));
            Console.SetCursorPosition(0, currentLineCursor);
        }
        static void print_current_status_read(int total)
        {
            while (print_status_read)
            {
                Console.Write("books parsed:\t" + Book.books_parsed.ToString() + "/" + total.ToString() + "\t" + Math.Round((double)Book.books_parsed / total * 100, 0) + "%");
                Thread.Sleep(1000 * 10);
                ClearCurrentConsoleLine();
            }
            Console.WriteLine();
        }
        static void print_current_status_write(int total)
        {
            while (print_status_db)
            {
                Console.Write("books added:\t" + Book.books_added.ToString() + "/" + total.ToString() + "\t" + Math.Round((double)Book.books_added / total * 100, 0) + "%");
                Thread.Sleep(1000 * 10);
                ClearCurrentConsoleLine();
            }
        }



        static void parse_all_books_to_DB(int books_to_read, bool recreate_DB = false)
        {

            Console.WriteLine("Wait untill all urls are parsed...");
            Task log_books_readed = Task.Factory.StartNew(() => print_current_status_read(books_to_read));

            for (int i = 47; i <= books_to_read; i++)
            {
                string url = libri_book_by_id_url + i.ToString();
                Book book = new Book(url);
            }

            Task.WaitAll(Book.booksTasksParsing.ToArray()); // Wait untill all books are ready
            print_status_read = false;
            print_status_db = true;
            Console.WriteLine("\nAll books are ready\n");

            Task log_books_added = Task.Factory.StartNew(() => print_current_status_write(Book.libriDB.Count()));
            Book.create_table(libri_db_path, delete_old: recreate_DB);
            Console.WriteLine("\nWait untill all books are added to db...");

            foreach (Book b in Book.libriDB) { b.addToDB(libri_db_path); }

            Task.WaitAll(Book.booksTasksWritingToDB.ToArray());
            Console.WriteLine("\nAll books are added\n\n" + Book.libriDB.Count() + " books are added");
            print_status_db = false;
        }
        public static long GetFileSize(string url)
        {
            Console.WriteLine(url);
            long result = -1;

            System.Net.WebRequest req = System.Net.WebRequest.Create(url);
            req.Method = "HEAD";
            using (System.Net.WebResponse resp = req.GetResponse())
            {
                if (long.TryParse(resp.Headers.Get("Content-Length"), out long ContentLength))
                {
                    result = ContentLength;
                }
            }

            return result;
        }
        static void Main(string[] args)
        {
            //Total number of IDs(not number of books) are not known
            //18.10.2019 is ~14150 IDs for ~13000+ books 
            //and 13890 books that includes ones that are in process       
            Book.init_semaphores(max_thread: 10000);
            parse_all_books_to_DB(books_to_read: 15000);

            foreach (var book in Book.libriDB)
            {
                //Console.WriteLine(book.id + "\t" + book.url_gutenberg + "\t" + book.url_wikisource);
                book.addTextUrl(libri_db_path);
            }

            while (Process.GetCurrentProcess().Threads.Count > 0)
            { }
            Console.WriteLine("No threads left");

            Console.ReadKey();
        }
    }
}
