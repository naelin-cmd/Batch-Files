using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

class Batchfiles2
{

    static void Main(string[] args)
    {
        string rootFolderPath = "X:\\1. Book ISBNs\\Takealot Online  DMS\\Book number\\Input\\Saturday 2.12.2023";
        string jsonFilePath = "Y:\\LSI\\DMS_ORDERS\\new_pod_orders_2023-12-07T16-28-03.json";
        string stateFilePath = "X:\\5. Users\\Naelin\\DMS Work\\State\\state.txt";
        string c = "DMS";

        // Initialize global unique number
        if (Directory.Exists(rootFolderPath))
        {
            int uniqueNumberCounter = 1;
            // Load JSON file
            string jsonContent = File.ReadAllText(jsonFilePath);
            JObject jsonObject = JObject.Parse(jsonContent);
            Dictionary<string, Dictionary<string, int>> isbnDmsUniqueNumbers = LoadState(stateFilePath);

            char currentFoldType = 'N';
            char currentLamination = 'N';
            char currentTextPaperColor = 'N';

            // Get all folder paths
            string[] folderPaths = Directory.GetDirectories(rootFolderPath);

            // Get all folder paths for the first pass (Double Fold, Single Fold, Saddle)
            string[] firstPassFolders = folderPaths
                .Where(ShouldProcessFolderFirstPass)
                .ToArray();

            // Process the first pass folders
            foreach (string folderPath in firstPassFolders)
            {
                int i = 1; // Initialize i here
                ProcessFolder(folderPath, ref uniqueNumberCounter, isbnDmsUniqueNumbers, jsonObject, c, stateFilePath, i);
                SaveState(isbnDmsUniqueNumbers, stateFilePath, uniqueNumberCounter);
                i++;
            }

            // Get all folder paths for the second pass (Covers)
            string[] secondPassFolders = folderPaths
                .Where(ShouldProcessFolderSecondPass)
                .ToArray();

            // Process the second pass folders
            foreach (string folderPath in secondPassFolders)
            {
                int i = 1; // Initialize i here or adjust based on your logic
                ProcessFolder(folderPath, ref uniqueNumberCounter, isbnDmsUniqueNumbers, jsonObject, c, stateFilePath, i);
                SaveState(isbnDmsUniqueNumbers, stateFilePath, uniqueNumberCounter);
                i++;
            }

            // Reset uniqueNumberCounter before processing the "Covers" folder


            // Get all folder paths for the third pass (Saddle)
        }
        else
        {
            Console.WriteLine($"Directory not found: {rootFolderPath}");
            // Handle the missing directory scenario as needed.
        }
    }


    static IEnumerable<string> GetPdfFiles(string folderPath)
    {
        if (!Directory.Exists(folderPath))
        {
            Console.WriteLine($"Directory not found: {folderPath}");
            return Enumerable.Empty<string>();
        }

        return Directory.EnumerateFiles(folderPath, "*.pdf", SearchOption.AllDirectories);
    }


    static void ProcessFolder(string folderPath, ref int uniqueNumberCounter, Dictionary<string, Dictionary<string, int>> isbnDmsUniqueNumbers, JObject jsonObject, string c, string stateFilePath, int i)
    {
        Dictionary<string, int> folderUniqueNumberCounters = new Dictionary<string, int>();

        // Check if the current folder should be processed (either first pass or second pass)
        if (ShouldProcessFolderFirstPass(folderPath) || ShouldProcessFolderSecondPass(folderPath))
        {
            if (!folderUniqueNumberCounters.TryGetValue(folderPath, out int folderUniqueNumber))
            {
                // Assign the global unique number as the folder's starting number
                folderUniqueNumber = uniqueNumberCounter;

                // Update the dictionary with the folder's unique number
                folderUniqueNumberCounters[folderPath] = folderUniqueNumber;
            }

            foreach (string pdfFile in GetPdfFiles(folderPath))
            {
                Console.WriteLine($"Processing PDF file: {pdfFile}");

                var (isbn1, dmsNumber1, isbn2, dmsNumber2) = GetISBNAndDMSFromPDFFileName(pdfFile);

                if (!string.IsNullOrEmpty(isbn1) && !string.IsNullOrEmpty(dmsNumber1) &&
                    !string.IsNullOrEmpty(isbn2) && !string.IsNullOrEmpty(dmsNumber2))
                {
                    // Process for Set 1
                    int uniqueNumber1 = GetNextUniqueNumber(isbnDmsUniqueNumbers, isbn1, dmsNumber1, ref uniqueNumberCounter);
                    ProcessSet(pdfFile, isbn1, dmsNumber1, uniqueNumber1, i, isbnDmsUniqueNumbers, jsonObject, c, folderPath, ref folderUniqueNumber);

                    // Process for Set 2
                    int uniqueNumber2 = GetNextUniqueNumber(isbnDmsUniqueNumbers, isbn2, dmsNumber2, ref uniqueNumberCounter);
                    ProcessSet(pdfFile, isbn2, dmsNumber2, uniqueNumber2, i, isbnDmsUniqueNumbers, jsonObject, c, folderPath, ref folderUniqueNumber);
                }
                RenamePDFs(folderPath, isbnDmsUniqueNumbers, ref uniqueNumberCounter, stateFilePath);
            }

            // Save state after processing each folder
            SaveState(isbnDmsUniqueNumbers, stateFilePath, uniqueNumberCounter);
        }
    }

    static bool ShouldProcessFolderFirstPass(string folderPath)
    {
        return folderPath.Contains("Double Fold") || folderPath.Contains("Single Fold") || folderPath.Contains("Saddle");

    }

    // New method to determine whether to process folders in the second pass
    static bool ShouldProcessFolderSecondPass(string folderPath)
    {
        return folderPath.Contains("Covers");
    }



    static void ProcessSet(string pdfFile, string isbn, string dmsNumber, int uniqueNumber, int i, Dictionary<string, Dictionary<string, int>> isbnDmsUniqueNumbers, JObject jsonObject, string c, string folderPath, ref int folderUniqueNumber)
    {
      

        dmsNumber = c + dmsNumber;

        JToken book = jsonObject.SelectTokens("$.orders[?(@.dms_order_number == '" + dmsNumber + "' && @.order_details[*].isbn == '" + isbn + "')]").FirstOrDefault();

        if (book == null)
        {
            return;
        }

        JToken orderDetails = book["order_details"]?.FirstOrDefault(details => details["isbn"].ToString() == isbn);

        if (orderDetails == null)
        {
            return;
        }

        string widthString = orderDetails["width"]?.ToString() ?? "";
        string foldType = (string.IsNullOrEmpty(widthString) || widthString.CompareTo("152") < 0) ? "Double Fold" : "Single Fold";
        char foldTypeLetter = !string.IsNullOrEmpty(foldType) ? foldType[0] : 'N';

        // Determine the folder-specific unique number counter
        int startingQuantity;

        if (!int.TryParse(orderDetails["qty"].ToString(), out startingQuantity))
        {
            Console.WriteLine($"Invalid quantity for DMS {dmsNumber} and ISBN {isbn}");
            return;
        }

        string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(pdfFile);
        string lamination = orderDetails["lamination"]?.ToString() ?? "";
        string textPaperColor = orderDetails["text_paper_color"]?.ToString() ?? "";
        char laminationLetter = !string.IsNullOrEmpty(lamination) ? lamination[0] : 'N';
        char textPaperColorLetter = !string.IsNullOrEmpty(textPaperColor) ? textPaperColor[0] : 'N';

        // Generate a new folder-specific number starting from "001"
        string newName = GetNewName(i);
        //Array.Sort(folderPath.ToArray());
        // Construct the new file name with the determined sorting prefix and folder-specific number


        string newFileName = $"{startingQuantity}._{uniqueNumber}{foldTypeLetter}{laminationLetter}{textPaperColorLetter}_{fileNameWithoutExtension}.pdf";




        uniqueNumber++;
        folderUniqueNumber++;

        // Construct the new file path within the existing folder
        string targetFolder = (laminationLetter == 'G') ? "Gloss" : ((laminationLetter == 'M') ? "Matt" : "");
        string targetFolder2 = (foldTypeLetter == 'S') ? "Single Fold" : ((foldTypeLetter == 'D') ? "DoubleFold" : "");

        if (folderPath.Contains("Double Fold"))
        {
            targetFolder2 = "";
        }
        else if (folderPath.Contains("Single Fold"))
        {
            targetFolder2 = "";
        }

        // Construct the new file path within the existing folder and move the file to the target folder
        string newFilePath = Path.Combine(Path.GetDirectoryName(pdfFile), targetFolder, targetFolder2, newFileName);

        // Ensure the target folder exists, create it if necessary
        Directory.CreateDirectory(Path.Combine(Path.GetDirectoryName(pdfFile), targetFolder, targetFolder2));

        // Move the file to the new path
        if (File.Exists(pdfFile))
        {
            // Move the file
            File.Move(pdfFile, newFilePath);
            Console.WriteLine($"Moved to {targetFolder}: {pdfFile} -> {newFileName}");
        }
        else
        {
            Console.WriteLine($"File not found: {pdfFile}");
        }
    }

    static void RenamePDFs(string folderPath, Dictionary<string, Dictionary<string, int>> isbnDmsUniqueNumbers, ref int uniqueNumberCounter, string stateFilePath)
    {

        // Check if the folder exists
        if (!Directory.Exists(folderPath))
        {
            Console.WriteLine($"Directory not found: {folderPath}");
            return;
        }

        // Get a list of all files in the folder
        string[] files = Directory.GetFiles(folderPath, "*.pdf");

        // Sort the PDF files
        Array.Sort(files);

        // Rename PDF files to '001.pdf', '002.pdf', '003.pdf', etc.
        for (int i = 0; i < files.Length; i++)
        {
            string newNumber = GetNewName(uniqueNumberCounter + i);
            string newPath = Path.Combine(folderPath, $"{newNumber}{Path.GetExtension(files[i])}");
            uniqueNumberCounter++;
            File.Move(files[i], newPath);
        }

        // Save state after renaming
        SaveState(isbnDmsUniqueNumbers, stateFilePath, uniqueNumberCounter);
    }


    static string GetNewName(int number)
    {
        int i = 1;
        return $"{number} + {i++}:D3"; // Format number with leading zeros

    }


    // Helper method to get the folder-specific unique number counter

    // Declare a static dictionary to store folder-specific unique number counters



    static void SaveState(Dictionary<string, Dictionary<string, int>> state, string stateFilePath, int uniqueNumberCounter)
    {
        using (StreamWriter writer = new StreamWriter(stateFilePath))
        {
            foreach (var isbn in state.Keys)
            {
                foreach (var dmsNumber in state[isbn].Keys)
                {
                    // Write each entry with the same unique number and 2-letter code for the given ISBN and DMS
                    int uniqueNumber = state[isbn][dmsNumber];

                    writer.WriteLine($"{isbn}:{dmsNumber}:{uniqueNumber}");
                }
            }
        }
    }


    static (string isbn1, string dmsNumber1, string isbn2, string dmsNumber2) GetISBNAndDMSFromPDFFileName(string pdfFilePath)
    {
        string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(pdfFilePath);
        string[] fileNameParts = fileNameWithoutExtension.Split('_');

        // Ensure that fileNameParts has at least 8 elements (6+2) for both sets of indices
        if (fileNameParts.Length >= 7)
        {
            string dmsNumber1 = fileNameParts[6];
            string isbn1 = fileNameParts[5];

            string dmsNumber2 = fileNameParts[5];
            string isbn2 = fileNameParts[4];

            return (isbn1, dmsNumber1, isbn2, dmsNumber2);
        }

        return (null, null, null, null);
    }


    static Dictionary<string, Dictionary<string, int>> LoadState(string stateFilePath)
    {
        Dictionary<string, Dictionary<string, int>> state = new Dictionary<string, Dictionary<string, int>>();

        if (File.Exists(stateFilePath))
        {
            string[] lines = File.ReadAllLines(stateFilePath);

            foreach (var line in lines)
            {
                string[] parts = line.Split(':');

                if (parts.Length == 3) // Expecting three parts: ISBN, DMS Number, and Unique Number
                {
                    string isbn = parts[0];
                    string dmsNumber = parts[1];
                    int uniqueNumber = 1;

                    if (int.TryParse(parts[2], out uniqueNumber))
                    {
                        // Ensure the ISBN key exists in the outer dictionary
                        if (!state.ContainsKey(isbn))
                        {
                            state[isbn] = new Dictionary<string, int>();
                        }

                        // Ensure the DMS key exists in the nested dictionary
                        if (!state[isbn].ContainsKey(dmsNumber))
                        {
                            state[isbn][dmsNumber] = uniqueNumber;
                        }
                    }
                }
            }
        }

        return state;
    }






    static Dictionary<string, Dictionary<string, int>> isbnDmsUniqueNumbers = new Dictionary<string, Dictionary<string, int>>();



    static int GetNextUniqueNumber(Dictionary<string, Dictionary<string, int>> isbnDmsUniqueNumbers, string isbn, string dmsNumber, ref int uniqueNumberCounter)
    {
        // Check if the ISBN key exists in the outer dictionary
        if (!isbnDmsUniqueNumbers.TryGetValue(isbn, out var dmsDictionary))
        {
            dmsDictionary = new Dictionary<string, int>();
            isbnDmsUniqueNumbers[isbn] = dmsDictionary;
        }

        if (dmsDictionary.TryGetValue(dmsNumber, out var uniqueNumber))
        {
            // Use the retrieved unique number
            return uniqueNumber;
        }
        else
        {
            // Add new key-value pair and handle missing key scenario
            dmsDictionary[dmsNumber] = uniqueNumberCounter; // Add a new key-value pair to the dictionary
                                                            // Increment the global unique number
            uniqueNumberCounter++;

            // Format the unique number as a 3-digit string (e.g., "001")
            string formattedUniqueNumber = uniqueNumber.ToString("D3");

            return int.Parse(formattedUniqueNumber); // Return the formatted unique number as the unique identifier
        }
    }
}

// Placeholder methods, replace with your actual implementation
