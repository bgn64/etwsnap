using System.Xml.Linq;
using ETWSnap;

namespace ETWSnap.WPR;

/// <summary>
/// Modifies WPRP (Windows Performance Recorder Profile) files
/// </summary>
public class WprpModifier
{
    private const string EtwSnapCollectorId = "EventCollector_ETWSnap";

    /// <summary>
    /// Adds the ETWSnap event provider to the default profile in a WPRP file
    /// </summary>
    /// <param name="inputFilePath">Path to the input WPRP file to read</param>
    /// <param name="outputFilePath">Path to the output WPRP file to write</param>
    /// <returns>True if successful, false otherwise</returns>
    public static bool AddEtwSnapProviderToDefaultProfile(string inputFilePath, string outputFilePath)
    {
        try
        {
            // Validate input file exists
            if (!File.Exists(inputFilePath))
            {
                Console.Error.WriteLine($"Error: Input WPRP file not found: {inputFilePath}");
                return false;
            }

            // Load the XML document
            XDocument doc = XDocument.Load(inputFilePath);

            // Detect the namespace used in the document
            XNamespace ns = doc.Root?.Name.Namespace ?? XNamespace.None;

            // Find the default profile
            var defaultProfile = FindDefaultProfile(doc, ns);
            if (defaultProfile == null)
            {
                Console.Error.WriteLine("Error: No profile with Default=\"true\" found in WPRP file");
                return false;
            }

            // Get or create the Collectors element
            var collectorsElement = defaultProfile.Element(ns + "Collectors");
            if (collectorsElement == null)
            {
                Console.Error.WriteLine("Error: No Collectors element found in default profile");
                return false;
            }

            // Check if ETWSnap collector already exists
            var existingCollector = collectorsElement.Elements(ns + "EventCollectorId")
                .FirstOrDefault(e => e.Attribute("Value")?.Value == EtwSnapCollectorId);

            if (existingCollector != null)
            {
                Console.WriteLine($"ETWSnap provider already exists in profile '{defaultProfile.Attribute("Name")?.Value}'");
                Console.WriteLine($"  EventCollectorId: {EtwSnapCollectorId}");
                return true;
            }

            // Create the new EventCollectorId element with inline provider
            var newEventCollector = CreateEtwSnapEventCollector(ns);

            // Add the new EventCollectorId at the end of Collectors
            collectorsElement.Add(newEventCollector);

            // Save the modified document to output file
            doc.Save(outputFilePath);

            Console.WriteLine($"Successfully added ETWSnap provider to profile '{defaultProfile.Attribute("Name")?.Value}'");
            Console.WriteLine($"  EventCollectorId: {EtwSnapCollectorId}");
            Console.WriteLine($"  Provider Name: {ETWSnapConstants.ProviderName}");
            Console.WriteLine($"  Provider GUID: {ETWSnapConstants.ProviderGuid}");

            return true;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error modifying WPRP file: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Finds the profile with Default="true" attribute
    /// </summary>
    private static XElement? FindDefaultProfile(XDocument doc, XNamespace ns)
    {
        // Search for Profile elements with Default="true"
        var profiles = doc.Descendants(ns + "Profile");
        
        foreach (var profile in profiles)
        {
            var defaultAttr = profile.Attribute("Default");
            if (defaultAttr != null && defaultAttr.Value.Equals("true", StringComparison.OrdinalIgnoreCase))
            {
                return profile;
            }
        }

        return null;
    }

    /// <summary>
    /// Creates an EventCollectorId element with inline ETWSnap provider definition
    /// </summary>
    private static XElement CreateEtwSnapEventCollector(XNamespace ns)
    {
        // Create EventCollectorId element
        var eventCollectorId = new XElement(ns + "EventCollectorId",
            new XAttribute("Value", EtwSnapCollectorId));

        // Create EventProviders container
        var eventProviders = new XElement(ns + "EventProviders");

        // Create inline EventProvider definition for ETWSnap
        // Following the pattern from the WPRP documentation for inline providers
        var eventProvider = new XElement(ns + "EventProvider",
            new XAttribute("Id", "EventProvider_ETWSnap"),
            new XAttribute("Name", ETWSnapConstants.ProviderGuid));

        // Add the provider to the EventProviders collection
        eventProviders.Add(eventProvider);

        // Add EventProviders to EventCollectorId
        eventCollectorId.Add(eventProviders);

        return eventCollectorId;
    }
}
