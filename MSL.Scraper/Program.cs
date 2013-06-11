﻿using AForge.Video.FFMPEG;
using HtmlAgilityPack;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.Threading;
using AForge.Imaging.Filters;
using System.Diagnostics;

namespace MSL.Scraper
{
    public class Cam
    {
        public string CamName { get; set; }
        public string CamContainer { get; set; }

        public Cam(string camName, string camContainer)
        {
            this.CamName = camName;
            this.CamContainer = camContainer;
        }
    }

    public static class MslCamConstants
    {
        public const string RawImageUrl = @"http://mars.jpl.nasa.gov/msl/multimedia/raw/";
        public const int MaxDownloadAttempts = 10;

        public static Cam FrontHazcamLeft = new Cam("Front Hazcam: Left", "Front Hazard Avoidance Cameras");
        public static Cam FrontHazcamRight = new Cam("Front Hazcam: Right", "Front Hazard Avoidance Cameras");
        public static Cam RearHazcamLeft = new Cam("Rear Hazcam: Left", "Rear Hazard Avoidance Cameras");
        public static Cam RearHazcamRight = new Cam("Rear Hazcam: Right", "Rear Hazard Avoidance Cameras");
        public static Cam NavcamLeft = new Cam("Navcam: Left", "Left Navigation Camera");
        public static Cam NavcamRight = new Cam("Navcam: Right", "Right Navigation Camera");

        public const string FullDataProductName = "FULL"; 
        public static Size FullDataProductSize = new Size(1024, 1024);

        public const string SaveBaseDirectory = @"C:\MSLScraper\";

        public const int VideoFramesPerImage = 4;
    }

    class Program
    {
        private static ConcurrentQueue<Exception> errorCollection = new ConcurrentQueue<Exception>();
        private static HtmlDocument basePage;

        static void Main(string[] args)
        {
            List<Cam> camsToProcess = new List<Cam>()
            {
                MslCamConstants.FrontHazcamLeft,
                MslCamConstants.FrontHazcamRight,
                //MslCamConstants.RearHazcamLeft,
                //MslCamConstants.RearHazcamRight,
                //MslCamConstants.NavcamLeft,
                //MslCamConstants.NavcamRight
            };

            foreach (var cam in camsToProcess)
            {
                try
                {
                    DownloadImages(cam);

                    CreateVideo(cam);
                }
                catch (Exception ex)
                {
                    string errorMessage = String.Format("ERROR while processing {0}", cam.CamName);
                    Console.WriteLine(String.Format("{0}: {1}", errorMessage, ex.Message));
                    errorCollection.Enqueue(new Exception(errorMessage, ex));
                }
            }

            try
            {
                TryCreate3DVideos(camsToProcess);
            }
            catch (Exception ex)
            {
                string errorMessage = String.Format("ERROR while processing 3D Videos");
                Console.WriteLine(String.Format("{0}: {1}", errorMessage, ex.Message));
                errorCollection.Enqueue(new Exception(errorMessage, ex));
            }

            Console.Clear();
            if (errorCollection.Count > 0)
            {
                Console.WriteLine("There were errors during processing, you may need to try again...");
                foreach (var error in errorCollection)
                {
                    Console.WriteLine(String.Format("{0}: {1}", error.Message, error.InnerException.Message));
                }
            }

            Console.WriteLine();
            Console.WriteLine("Process Complete, press any key to close...");
            Console.WriteLine();
            Console.Read();
        }

        private static void DownloadImages(Cam cam)
        {
            int solsProcessed = 0;
            int imagesProcessed = 0;
            int imagesDownloaded = 0;

            if (basePage == null)
            {
                Console.Clear();
                Console.WriteLine(String.Format("Attempting to contact {0}", MslCamConstants.RawImageUrl));

                HtmlWeb baseWeb = new HtmlWeb();
                basePage = TryLoadDoc(MslCamConstants.RawImageUrl, baseWeb);
            }

            HtmlNodeCollection baseContainers = basePage.DocumentNode.SelectNodes(@"//div[@class='image_set_container']");

            foreach (HtmlNode container in baseContainers)
            {
                HtmlNodeCollection baseSolLinks = container.SelectNodes(@".//a[starts-with(@href,'./?s=')]");

                if (container.ChildNodes[1].InnerText.Contains(cam.CamContainer))
                {
                    Console.Clear();
                    Console.WriteLine(String.Format("Attempting to download new images for {0}...", cam.CamName));

                    Parallel.ForEach(baseSolLinks, solLink =>
                    {
                        if (solLink.InnerHtml.StartsWith("Sol"))
                        {
                            string[] solTitles = solLink.InnerHtml.Split(new char[] { '\n' });
                            int solNumber = int.Parse(solTitles[1]);
                            string solUrl = solLink.Attributes["href"].Value.Substring(1);

                            try
                            {
                                HtmlWeb solWeb = new HtmlWeb();
                                HtmlDocument solDoc;
                                solDoc = TryLoadDoc(String.Format("{0}{1}", MslCamConstants.RawImageUrl, solUrl), solWeb);

                                HtmlNode solContent = solDoc.DocumentNode.SelectSingleNode(@"//td[@class='pageContent']");
                                HtmlNodeCollection solTables = solContent.SelectNodes(@".//table");
                                List<HtmlNode> solContainers = new List<HtmlNode>();

                                HtmlNode solStrong = solDoc.DocumentNode.SelectSingleNode(@"/html[1]/body[1]/div[1]/div[1]/div[1]/div[3]/table[1]/tr[2]/td[1]/div[2]/table[1]/tr[4]");
                                if (solStrong != null && solStrong.InnerText.Contains(MslCamConstants.FullDataProductName))
                                {
                                    foreach (HtmlNode tr in solStrong.ParentNode.ChildNodes)
                                    {
                                        if (!tr.InnerText.StartsWith(MslCamConstants.FullDataProductName) && tr.InnerText.Contains("Data Product"))
                                            break;

                                        HtmlNodeCollection imgDataDivs = tr.SelectNodes(@".//div[@class='RawImageCaption']");
                                        if (imgDataDivs != null)
                                            solContainers.AddRange(imgDataDivs.ToList());
                                    }
                                }

                                if (solContainers != null)
                                    Parallel.ForEach(solContainers, solImage =>
                                    {
                                        string[] imgTitles = solImage.InnerText.Split(new char[] { '\n', '&' }, StringSplitOptions.RemoveEmptyEntries);
                                        string imgCamName = imgTitles[0];
                                        DateTime imgTimeStamp = DateTime.Parse(imgTitles[1]);
                                        if (imgCamName.Contains(cam.CamName))
                                        {
                                            try
                                            {

                                                using (MSLScraperEntities mslContext = new MSLScraperEntities())
                                                {
                                                    if (mslContext.SolImageData.Count(x => x.Cam == imgCamName && x.Sol == solNumber && x.TimeStamp == imgTimeStamp) == 0)
                                                    {
                                                        HtmlNode imgFullLinkNode = solDoc.DocumentNode.SelectSingleNode(solImage.XPath + @"/nobr[1]/div[1]/a[1]");
                                                        string imgFullLinkUrl = imgFullLinkNode.Attributes["href"].Value;

                                                        SolImageData newImageData = new SolImageData();
                                                        newImageData.Sol = solNumber;
                                                        newImageData.Cam = imgCamName;
                                                        newImageData.TimeStamp = imgTimeStamp;
                                                        newImageData.ImageUrl = imgFullLinkUrl;

                                                        Image bitmap = TryLoadImage(imgFullLinkUrl);

                                                        if (bitmap != null)
                                                        {
                                                            using (MemoryStream stream = new MemoryStream())
                                                            {
                                                                bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Bmp);
                                                                bitmap.Dispose();
                                                                stream.Position = 0;
                                                                byte[] data = new byte[stream.Length];
                                                                stream.Read(data, 0, (int)stream.Length);
                                                                newImageData.ImageData = data;
                                                            }

                                                            if (mslContext.SolImageData.Count(x => x.Cam == newImageData.Cam && x.Sol == newImageData.Sol && x.TimeStamp == newImageData.TimeStamp) == 0)
                                                            {
                                                                mslContext.SolImageData.Add(newImageData);
                                                                mslContext.SaveChanges();

                                                                imagesDownloaded++;
                                                                WriteDownloadProgress(cam, solsProcessed, imagesProcessed, imagesDownloaded);
                                                            }
                                                        }
                                                    }
                                                }

                                            }
                                            catch (Exception ex)
                                            {
                                                string errorMessage = String.Format("ERROR while downloading {0} Sol {1} Timestamp {2}", cam.CamName, solNumber, imgTitles[1]);
                                                Console.WriteLine(String.Format("{0}: {1}", errorMessage, ex.Message));
                                                errorCollection.Enqueue(new Exception(errorMessage, ex));
                                            }

                                            imagesProcessed++;
                                            WriteDownloadProgress(cam, solsProcessed, imagesProcessed, imagesDownloaded);
                                        }
                                    });
                            }
                            catch (Exception ex)
                            {
                                string errorMessage = String.Format("ERROR while downloading {0} Sol {1} images", cam.CamName, solNumber);
                                Console.WriteLine(String.Format("{0}: {1}", errorMessage, ex.Message));
                                errorCollection.Enqueue(new Exception(errorMessage, ex));
                            }

                            solsProcessed++;
                            WriteDownloadProgress(cam, solsProcessed, imagesProcessed, imagesDownloaded);
                        }
                    });
                }
            }
        }

        private static void CreateVideo(Cam cam)
        {
            if (cam == null) throw new ArgumentNullException("cam");

            string camSaveDirectory = PrepareSaveDirectory(cam.CamName);
            string videoFileName = String.Format("{0}{1} {2}.mpeg", camSaveDirectory, cam.CamName.Replace(":", ""), DateTime.Now.ToString("MMM-dd-yyyy-HH-mm"));

            if (File.Exists(videoFileName))
                File.Delete(videoFileName);

            try
            {
                using (VideoFileWriter videoWriter = new VideoFileWriter())
                {
                    videoWriter.Open(videoFileName, MslCamConstants.FullDataProductSize.Width, MslCamConstants.FullDataProductSize.Height);

                    int totalImageCount = 0;
                    using (MSLScraperEntities mslContext = new MSLScraperEntities())
                    {
                        totalImageCount = mslContext.SolImageData.Count(x => x.Cam.Contains(cam.CamName));
                    }

                    int take = 30;
                    int processedCount = 0;
                    for (int skip = 0; skip < totalImageCount; skip = skip + take)
                    {
                        using (MSLScraperEntities mslContext = new MSLScraperEntities())
                        {
                            var imagesToProcess = mslContext.SolImageData.Where(x => x.Cam.Contains(cam.CamName)).OrderBy(x => x.TimeStamp).Skip(skip).Take(take);

                            foreach (SolImageData solImage in imagesToProcess)
                            {
                                using (MemoryStream ms = new MemoryStream(solImage.ImageData))
                                using (Bitmap bitmap = (Bitmap)Image.FromStream(ms))
                                {
                                    if (bitmap.Width == videoWriter.Width && bitmap.Height == videoWriter.Height)
                                    {
                                        using (Bitmap newBitmap = new Bitmap(bitmap.Width, bitmap.Height))
                                        using (Graphics g = Graphics.FromImage(newBitmap))
                                        {
                                            g.DrawImage(bitmap, 0, 0);
                                            g.DrawString(String.Format("{0} - Sol: {1}", solImage.Cam, solImage.Sol), new Font(FontFamily.GenericSansSerif, 30, FontStyle.Bold), Brushes.White, new PointF(10, 10));

                                            for (int i = 0; i < MslCamConstants.VideoFramesPerImage; i++)
                                            {
                                                videoWriter.WriteVideoFrame(newBitmap);
                                            }
                                        }
                                    }
                                }

                                processedCount++;
                                Console.Clear();
                                Console.WriteLine(string.Format("video processing: {0} of {1} images processed for {2}", processedCount, totalImageCount, cam.CamName));
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                string errorMessage = String.Format("ERROR while creating video for {0}", cam.CamName);
                Console.WriteLine(String.Format("{0}: {1}", errorMessage, ex.Message));
                errorCollection.Enqueue(new Exception(errorMessage, ex));
            }
        }

        private static void TryCreate3DVideos(List<Cam> camsToProcess)
        {
            if (camsToProcess == null) throw new ArgumentNullException("camsToProcess");

            if (camsToProcess.Contains(MslCamConstants.FrontHazcamLeft) && camsToProcess.Contains(MslCamConstants.FrontHazcamRight))
            {
                Create3DVideo(MslCamConstants.FrontHazcamLeft, MslCamConstants.FrontHazcamRight);
            }
            else if (camsToProcess.Contains(MslCamConstants.RearHazcamLeft) && camsToProcess.Contains(MslCamConstants.RearHazcamRight))
            {
                Create3DVideo(MslCamConstants.RearHazcamLeft, MslCamConstants.RearHazcamRight);
            }
            else if (camsToProcess.Contains(MslCamConstants.NavcamLeft) && camsToProcess.Contains(MslCamConstants.NavcamRight))
            {
                Create3DVideo(MslCamConstants.NavcamLeft, MslCamConstants.NavcamRight);
            }
        }

        private static void Create3DVideo(Cam leftCam, Cam rightCam)
        {
            if (leftCam == null) throw new ArgumentNullException("leftCam");
            if (rightCam == null) throw new ArgumentNullException("rightCam");

            string camSaveDirectory = PrepareSaveDirectory(leftCam.CamContainer);
            string videoFileName = String.Format("{0}{1} {2}.mpeg", camSaveDirectory, leftCam.CamContainer, DateTime.Now.ToString("MMM-dd-yyyy-HH-mm"));

            if (File.Exists(videoFileName))
                File.Delete(videoFileName);

            try
            {
                using (VideoFileWriter videoWriter = new VideoFileWriter())
                {
                    videoWriter.Open(videoFileName, MslCamConstants.FullDataProductSize.Width, MslCamConstants.FullDataProductSize.Height);

                    int totalImageCount = 0;
                    using (MSLScraperEntities mslContext = new MSLScraperEntities())
                    {
                        totalImageCount = (from A in
                                               (from a1 in mslContext.SolImageData where a1.Cam.Contains(leftCam.CamName) select a1)
                                           from B in
                                               (from b1 in mslContext.SolImageData where b1.Cam.Contains(rightCam.CamName) select b1)
                                           where A.Sol == B.Sol && A.TimeStamp == B.TimeStamp select A).Count();
                    }

                    int take = 15;
                    int processedCount = 0;
                    for (int skip = 0; skip < totalImageCount; skip = skip + take)
                    {
                        using (MSLScraperEntities mslContext = new MSLScraperEntities())
                        {
                            var imagesToProcess = (from A in
                                                       (from a1 in mslContext.SolImageData where a1.Cam.Contains(leftCam.CamName) select a1)
                                                   from B in
                                                       (from b1 in mslContext.SolImageData where b1.Cam.Contains(rightCam.CamName) select b1)
                                                   where A.Sol == B.Sol && A.TimeStamp == B.TimeStamp
                                                   orderby A.TimeStamp
                                                   select new { LeftCam = A, RightCam = B }).Skip(skip).Take(take);
                            
                            foreach (var imagePair in imagesToProcess)
                            {
                                using (MemoryStream msLeft = new MemoryStream(imagePair.LeftCam.ImageData))
                                using (Bitmap bitmapLeft = (Bitmap)Image.FromStream(msLeft))
                                using (MemoryStream msRight = new MemoryStream(imagePair.RightCam.ImageData))
                                using (Bitmap bitmapRight = (Bitmap)Image.FromStream(msRight))
                                {
                                    if (bitmapLeft.Width == videoWriter.Width && bitmapLeft.Height == videoWriter.Height &&
                                        bitmapRight.Width == videoWriter.Width && bitmapRight.Height == videoWriter.Height)
                                    {
                                        StereoAnaglyph filter = new StereoAnaglyph(StereoAnaglyph.Algorithm.TrueAnaglyph);
                                        filter.OverlayImage = AForge.Imaging.Image.Clone(bitmapRight, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
                                        
                                        using (Bitmap bitmap3D = filter.Apply(AForge.Imaging.Image.Clone(bitmapLeft, System.Drawing.Imaging.PixelFormat.Format24bppRgb)))
                                        using (Bitmap newBitmap = new Bitmap(bitmapLeft.Width, bitmapLeft.Height))
                                        using (Graphics g = Graphics.FromImage(newBitmap))
                                        {
                                            g.DrawImage(bitmap3D, 0, 0);
                                            g.DrawString(String.Format("{0} - Sol: {1}", leftCam.CamContainer, imagePair.LeftCam.Sol), new Font(FontFamily.GenericSansSerif, 30, FontStyle.Bold), Brushes.White, new PointF(10, 10));

                                            for (int i = 0; i < MslCamConstants.VideoFramesPerImage; i++)
                                            {
                                                videoWriter.WriteVideoFrame(newBitmap);
                                            }
                                        }
                                    }
                                }

                                processedCount++;
                                Console.Clear();
                                Console.WriteLine(string.Format("3D processing: {0} of {1} images processed for {2}", processedCount, totalImageCount, leftCam.CamContainer));
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                string errorMessage = String.Format("ERROR while creating 3D video for {0}", leftCam.CamContainer);
                Console.WriteLine(String.Format("{0}: {1}", errorMessage, ex.Message));
                errorCollection.Enqueue(new Exception(errorMessage, ex));
            }
        }

        private static HtmlDocument TryLoadDoc(string url, HtmlWeb web)
        {
            if (String.IsNullOrWhiteSpace(url)) throw new ArgumentNullException("solUrl");
            if (web == null) throw new ArgumentNullException("solWeb");

            HtmlDocument solDoc = new HtmlDocument();
            int attempt = 0;

            while (true)
            {
                try
                {
                    solDoc = web.Load(url);
                    break;
                }
                catch (Exception)
                {
                    if (attempt < MslCamConstants.MaxDownloadAttempts)
                        attempt++;
                    else
                        throw;
                }
            }
            return solDoc;
        }

        private static Image TryLoadImage(string imgFullLinkUrl)
        {
            if (String.IsNullOrWhiteSpace(imgFullLinkUrl)) throw new ArgumentNullException("imgFullLinkUrl");

            Image bitmap;
            int attempt = 0;

            using (WebClient imgClient = new WebClient())
            {
                while (true)
                {
                    try
                    {
                        using (Stream stream = imgClient.OpenRead(new Uri(imgFullLinkUrl)))
                        {
                            bitmap = Bitmap.FromStream(stream);

                            if (bitmap.Width < MslCamConstants.FullDataProductSize.Width || bitmap.Height < MslCamConstants.FullDataProductSize.Height)
                            {
                                bitmap.Dispose();
                                bitmap = null;
                            }
                            break;
                        }
                    }
                    catch (Exception)
                    {
                        if (attempt < MslCamConstants.MaxDownloadAttempts)
                            attempt++;
                        else
                            throw;
                    }
                }
            }
            return bitmap;
        }

        private static string PrepareSaveDirectory(string camName)
        {
            if (String.IsNullOrWhiteSpace(camName)) throw new ArgumentNullException("camName");

            string camSaveDirectory = String.Format(@"{0}{1}\", MslCamConstants.SaveBaseDirectory, camName.Replace(":", ""));

            if (!Directory.Exists(camSaveDirectory))
                Directory.CreateDirectory(camSaveDirectory);

            return camSaveDirectory;
        }

        private static void WriteDownloadProgress(Cam cam, int solsProcessed, int imagesProcessed, int imagesDownloaded)
        {
            if (cam == null) throw new ArgumentNullException("cam");

            Console.Clear();
            Console.WriteLine(String.Format("Processing images for {0}: ", cam.CamName));
            Console.WriteLine(String.Format("# of Sols processed:    {0}", solsProcessed));
            Console.WriteLine(String.Format("# of Images processed:  {0}", imagesProcessed));
            Console.WriteLine(String.Format("# of Images downloaded: {0}", imagesDownloaded));
        }
    }
}
