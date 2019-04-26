using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Daimler_Face_App.Models;
using System.IO;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.ProjectOxford.Face;
using Microsoft.ProjectOxford.Face.Contract;

namespace Daimler_Face_App.Controllers
{
    public class HomeController : Controller
    {
        private readonly IHostingEnvironment _hostingEnvironment;

        // TODO: move to app setting
        const string personGroupId = "<personGroupId>";
        const string subscriptionKey = "<subscription key from azure>";
        const string apiRootUrl = "<API URL>";// e.g. https://southeastasia.api.cognitive.microsoft.com/face/v1.0


        public HomeController(IHostingEnvironment hostingEnvironment)
        {
            _hostingEnvironment = hostingEnvironment;
        }

        public IActionResult Index()
        {
            return View();
        }

        [HttpPost]
        public async Task<string> Capture()
        {
            //1 = train
            //2 = recognise
            int choice=0;
            string name="";

            choice = Convert.ToInt32(HttpContext.Request.Query["choice"].ToString());
            name = HttpContext.Request.Query["name"].ToString();

            var result = "Something wrong";

            var files = HttpContext.Request.Form.Files;
            if (files != null)
            {
                foreach (var file in files)
                {
                    if (file.Length > 0)
                    {
                        // Getting Filename  
                        var fileName = file.FileName;
                        // Unique filename "Guid"  
                        var myUniqueFileName = Convert.ToString(Guid.NewGuid());
                        // Getting Extension  
                        var fileExtension = Path.GetExtension(fileName);
                        // Concating filename + fileExtension (unique filename)  
                        var newFileName = string.Concat(myUniqueFileName, fileExtension);
                        //  Generating Path to store photo   
                        var filepath = Path.Combine(_hostingEnvironment.WebRootPath, "CameraPhotos") + $@"\{newFileName}";

                      

                        if (!string.IsNullOrEmpty(filepath))
                        {
                            // Storing Image in Folder  
                            StoreInFolder(file, filepath);
                        }

                        if (choice == 1)
                        {
                            result = await Identify(filepath);                            
                        }
                        else if (choice == 2) {
                            result = await Train(filepath, name);
                        }

                    }
                }
                return result;
            }
            else
            {
                return "No Files";
            }
        }

        private void StoreInFolder(IFormFile file, string fileName)
        {
            using (FileStream fs = System.IO.File.Create(fileName))
            {
                file.CopyTo(fs);
                fs.Flush();
            }
        }

        private async Task<string> Train(string testImageFile, string name) {

            string result = "";

            try
            {
                Nullable<System.Guid> personId=null;
                 
                var faceServiceClient = new FaceServiceClient(subscriptionKey, apiRootUrl);


                // I have already created a person group. If you haven't created please use the below code to create before get
                // await faceServiceClient.CreatePersonGroupAsync(personGroupId, "<friendly name to your group>");                
                var personGroup =  await faceServiceClient.GetPersonGroupAsync(personGroupId);

                var persons = await faceServiceClient.GetPersonsAsync(personGroupId);

                if(persons != null)
                    foreach (var item in persons)
                    {
                        if (item.Name.Equals(name, StringComparison.InvariantCultureIgnoreCase)) {
                            personId = item.PersonId;
                        }
                    }

                if (personId == null) {
                    CreatePersonResult personCreate = await faceServiceClient.CreatePersonAsync(
                        // Id of the PersonGroup that the person belonged to
                        personGroupId,
                        // Name of the person
                        name
                    );
                    personId = personCreate.PersonId;
                }

                if (personId == null)
                    return "personId is null";

                using (Stream s = System.IO.File.OpenRead(testImageFile))
                {
                    // Detect faces in the image and add to Anna
                    await faceServiceClient.AddPersonFaceAsync(
                        personGroupId, personId.Value, s);
                }


                await faceServiceClient.TrainPersonGroupAsync(personGroupId);

                TrainingStatus trainingStatus = null;

                while (true)
                {
                    trainingStatus = await faceServiceClient.GetPersonGroupTrainingStatusAsync(personGroupId);

                    if (trainingStatus.Status != Status.Running)
                    {
                        break;
                    }

                    await Task.Delay(1000);
                }


                result = "Successfully trained";

            }
            catch (FaceAPIException e)
            {
                result = e.Message;
            }

            return result;

        }

        private async Task<string> Identify(string testImageFile) {

            string result = "";

            try
            {
              
                var faceServiceClient = new FaceServiceClient(subscriptionKey, apiRootUrl);

                using (Stream s = System.IO.File.OpenRead(testImageFile))
                {
                    var faces = await faceServiceClient.DetectAsync(s);
                    var faceIds = faces.Select(face => face.FaceId).ToArray();

                    var results = await faceServiceClient.IdentifyAsync(personGroupId, faceIds);
                    foreach (var identifyResult in results)
                    {
                       // Console.WriteLine("Result of face: {0}", identifyResult.FaceId);
                        if (identifyResult.Candidates.Length == 0)
                        {
                            result = "No one identified";
                        }
                        else
                        {
                            // Get top 1 among all candidates returned
                            var candidateId = identifyResult.Candidates[0].PersonId;
                            var person = await faceServiceClient.GetPersonAsync(personGroupId, candidateId);
                            result = $"Identified as {person.Name}";
                        }
                    }
                }
            }
            catch (FaceAPIException e)
            {
                result = e.Message;
            }
            return result;
        }



        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
