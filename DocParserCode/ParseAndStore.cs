using Azure;
using Azure.AI.FormRecognizer.DocumentAnalysis;
using Azure.Storage;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using DocParserCode.Enums;
using DocParserCode.Models;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace DocParserCode
{
    public class ParseAndStore
    {
        private const string StorageAccountName = "stdocreader";

        private readonly IConfiguration _configuration;
        private static readonly HttpClient Client = new();
        private static List<SurveyQuestion> fetchedQuestionsFromApi;

        public ParseAndStore(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        [FunctionName("ParseAndStore")]
        public async Task Run([BlobTrigger("uploads/{name}", Connection = "AzureWebJobsStorage")] Stream myBlob,
            string name,
            Uri uri,
            ILogger log,
            ExecutionContext context)
        {
            log.LogInformation($"FROM PARSE AND STORE: C# Blob trigger function Processed blob\n Name:{name} \n Size: {myBlob.Length} Bytes");

            string endpoint = "https://ai-docreader.cognitiveservices.azure.com/";
            string apiKey = _configuration["KeyVault:AiCognitiveKey"];
            AzureKeyCredential credential = new(apiKey);
            DocumentAnalysisClient client = new(new Uri(endpoint), credential);

            string modelId = "SurveyExtractionModel4";
            Uri fileUri = new(uri.AbsoluteUri);

            try
            {
                // TODO: check to ensure file exists before processing
                // Reason: Deployed active function app will process (steal) before the debug session
                // For debugging, first STOP the deployed function app


                AnalyzeDocumentOperation operation = await client.AnalyzeDocumentFromUriAsync(WaitUntil.Completed, modelId, fileUri);

                AnalyzeResult result = operation.Value;

                Console.WriteLine($"Document was analyzed with model with ID: {result.ModelId}");

                foreach (AnalyzedDocument document in result.Documents)
                {
                    Console.WriteLine($"Document of type: {document.DocumentType}");

                    // Get Survey Type

                    document.Fields.TryGetValue("SurveyType", out var surveyType);

                    string strSurveyType = surveyType.Content;

                    Console.WriteLine($"Survey Type Domestic or Foreign: {strSurveyType}");

                    // Request List of Questions by Survey Type

                    fetchedQuestionsFromApi = await FetchSurveyQuestions(strSurveyType);

                    // Build List of Answers For API

                    SurveyAnswerSubmit surveyAnswerSubmit = new()
                    {
                        SurveyAnswers = await BuildListOfAnswers(document.Fields)
                    };

                    int surveyTypeEnum = (int)Enum.Parse(typeof(SurveyTypeEnum.SurveyType), strSurveyType);

                    surveyAnswerSubmit.SurveyTypeId = surveyTypeEnum;

                    // Send List of Answers to API

                    var requestUri = "https://surveyapicjd.azurewebsites.net/api/Surveys/createsurvey";
                    //var requestUri = "https://localhost:7232/api/Surveys/createsurvey";

                    //JsonContent content = JsonContent.Create(surveyAnswerSubmit);
                    //HttpResponseMessage response = await Client.PostAsJsonAsync(requestUri, content);

                    string content = JsonSerializer.Serialize(surveyAnswerSubmit);

                    HttpRequestMessage request = new(HttpMethod.Post, requestUri)
                    {
                        Content = new StringContent(content, Encoding.UTF8, "application/json")
                    };

                    HttpResponseMessage response = await Client.SendAsync(request);

                    if (response.IsSuccessStatusCode)
                    {
                        Console.WriteLine($"Document {name} parsed and extracted data sent to the api");
                        log.LogInformation($"Document {name} parsed and extracted data sent to the api");

                        Task task = await MoveBlobToProcessed(name);

                        if (task.IsCompletedSuccessfully)
                        {
                            Console.WriteLine($"Document {name} moved to processed");
                            log.LogInformation($"Document {name} moved to processed");
                        }
                    }
                    else
                    {
                        log.LogError($"ERROR: {response.StatusCode}-{response.ReasonPhrase}");
                        Console.WriteLine("FAIL");
                    }
                }
            }
            catch (Exception ex)
            {
                log.LogError(ex, ex.Message);
                throw;
            }

        }

        private static async Task<List<SurveyAnswerProxy>> BuildListOfAnswers(IReadOnlyDictionary<string, DocumentField> fields)
        {
            List<SurveyAnswerProxy> answers = new();

            foreach (KeyValuePair<string, DocumentField> fieldKvp in fields)
            {
                string fieldName = fieldKvp.Key;
                DocumentField field = fieldKvp.Value;

                Console.WriteLine($"Field '{fieldName}': ");

                if (fieldName == "TableAnswers")
                {
                    var answerList = field.Value.AsList();

                    foreach (var answer in answerList)
                    {
                        var questionAnswers = answer.Value.AsDictionary();
                        SurveyQuestion currentQuestion = null;

                        foreach (var pairs in questionAnswers)
                        {

                            if (pairs.Key == "QUESTION")
                            {
                                currentQuestion = fetchedQuestionsFromApi.Find(x => x.QuestionText == pairs.Value.Content);

                                Console.WriteLine(pairs.Value.Content);
                            }

                            if (pairs.Key == "ANSWER")
                            {
                                // if content is null, coalesce to empty string for Json conversion, critical for successful response
                                answers.Add(new SurveyAnswerProxy
                                {
                                    SurveyQuestionId = currentQuestion.SurveyQuestionId,
                                    AnswerText = pairs.Value.Content ?? string.Empty,
                                });

                                Console.WriteLine(pairs.Value.Content ?? "null");
                            }
                        }
                    }
                }

                Console.WriteLine($"  Content: '{field.Content}'");
                Console.WriteLine($"  Confidence: '{field.Confidence}'");
            }

            return await Task.FromResult(answers);
        }

        private static async Task<List<SurveyQuestion>> FetchSurveyQuestions(string surveyType)
        {
            List<SurveyQuestion> questions = new();

            var requestUri = "https://surveyapicjd.azurewebsites.net/api/SurveyQuestions/getsurveyquestions";

            HttpRequestMessage newRequest = new(HttpMethod.Get, $"{requestUri}/{surveyType}");

            //Read Server Response
            HttpResponseMessage response = await Client.SendAsync(newRequest);

            if (response.IsSuccessStatusCode)
            {
                questions = await response.Content.ReadAsAsync<List<SurveyQuestion>>();
            }

            return await Task.FromResult(questions);
        }

        public async Task<Task> MoveBlobToProcessed(string fileName)
        {
            var srcContainer = GetBlobContainerClient("uploads");
            var targetContainer = GetBlobContainerClient("processed");

            // Get a BlobClient for the source blob
            BlobClient srcBlobClient = srcContainer.GetBlobClient(fileName);
            var srcUri = srcBlobClient.Uri;

            // Create a BlobClient for the destination blob
            BlobClient targetBlobClient = targetContainer.GetBlobClient(fileName);

            // Start copying the source blob to the destination blob
            await targetBlobClient.StartCopyFromUriAsync(srcUri);

            // Delete the source blob after copying is done
            await srcBlobClient.DeleteAsync(DeleteSnapshotsOption.IncludeSnapshots);

            return Task.CompletedTask;
        }

        private BlobContainerClient GetBlobContainerClient(string containerName)
        {
            // TODO: use options model to fetch storage key

            StorageSharedKeyCredential sharedKeyCredential = new(StorageAccountName, _configuration["KeyVault:StorageKey"].ToString());

            string blobUri = $"https://{StorageAccountName}.blob.core.windows.net/{containerName}";

            var blobContainerClient = new BlobContainerClient(new Uri(blobUri), sharedKeyCredential);

            return blobContainerClient;
        }
    }
}
