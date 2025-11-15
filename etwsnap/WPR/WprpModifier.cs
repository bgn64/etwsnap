using System.Xml.Linq;
using ETWSnap;

namespace ETWSnap.WPR;

/// <summary>
/// Modifies WPRP (Windows Performance Recorder Profile) files
/// </summary>
public class WprpModifier
{
    private const string EtwSnapCollectorId = "EventCollector_ETWSnap";
    private const string EtwSnapEventProviderId = "EventProvider_ETWSnap";

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

            // First, add the EventCollector definition at the top level if it doesn't exist
            if (!AddEventCollectorDefinition(doc, ns))
            {
                Console.Error.WriteLine("Error: Failed to add EventCollector definition");
                return false;
            }

            // Get or create the Collectors element in the profile
            var collectorsElement = defaultProfile.Element(ns + "Collectors");
            if (collectorsElement == null)
            {
                // Create Collectors element if it doesn't exist
                collectorsElement = new XElement(ns + "Collectors");
                defaultProfile.Add(collectorsElement);
            }

            // Check if ETWSnap collector reference already exists in the profile
            var existingCollectorRef = collectorsElement.Elements(ns + "EventCollectorId")
                .FirstOrDefault(e => e.Attribute("Value")?.Value == EtwSnapCollectorId);

            if (existingCollectorRef != null)
            {
                Logger.Info($"ETWSnap provider already exists in profile '{defaultProfile.Attribute("Name")?.Value}'");
                Logger.Info($"  EventCollectorId: {EtwSnapCollectorId}");
                return true;
            }

            // Create the EventCollectorId reference with inline EventProviders
            var eventCollectorIdRef = new XElement(ns + "EventCollectorId",
                new XAttribute("Value", EtwSnapCollectorId));

            // Create EventProviders container
            var eventProviders = new XElement(ns + "EventProviders");

            // Create inline EventProvider definition for ETWSnap
            var eventProvider = new XElement(ns + "EventProvider",
                new XAttribute("Id", EtwSnapEventProviderId),
                new XAttribute("Name", ETWSnapConstants.ProviderGuid),
                new XAttribute("NonPagedMemory", "true"),
                new XAttribute("Level", "5"));

            // Add the provider to the EventProviders collection
            eventProviders.Add(eventProvider);

            // Add EventProviders to EventCollectorId
            eventCollectorIdRef.Add(eventProviders);

            // Add the EventCollectorId reference to the profile's Collectors
            collectorsElement.Add(eventCollectorIdRef);

            // Save the modified document to output file
            doc.Save(outputFilePath);

            Logger.Info($"Successfully added ETWSnap provider to profile '{defaultProfile.Attribute("Name")?.Value}'");
            Logger.Info($"  EventCollectorId: {EtwSnapCollectorId}");
            Logger.Info($"  Provider Name: {ETWSnapConstants.ProviderName}");
            Logger.Info($"  Provider GUID: {ETWSnapConstants.ProviderGuid}");

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
    /// Adds the EventCollector definition to the top-level Profiles section if it doesn't already exist
    /// </summary>
    private static bool AddEventCollectorDefinition(XDocument doc, XNamespace ns)
    {
        var profilesElement = doc.Root?.Element(ns + "Profiles");
        if (profilesElement == null)
        {
            Console.Error.WriteLine("Error: No Profiles element found in WPRP file");
            return false;
        }

        // Check if ETWSnap EventCollector already exists
        var existingCollector = profilesElement.Elements(ns + "EventCollector")
            .FirstOrDefault(e => e.Attribute("Id")?.Value == EtwSnapCollectorId);

        if (existingCollector != null)
        {
            Logger.Info($"EventCollector definition '{EtwSnapCollectorId}' already exists");
            return true;
        }

        // Find the insertion point: after the last EventCollector, or after SystemCollector if no EventCollectors exist
        XElement? insertAfter = profilesElement.Elements(ns + "EventCollector").LastOrDefault();
        if (insertAfter == null)
        {
            // No EventCollectors exist, insert after the last SystemCollector
            insertAfter = profilesElement.Elements(ns + "SystemCollector").LastOrDefault();
        }

        if (insertAfter == null)
        {
            Console.Error.WriteLine("Error: No SystemCollector or EventCollector found to insert after");
            return false;
        }

        // Create the EventCollector definition
        var eventCollector = CreateEtwSnapEventCollector(ns);

        // Insert after the determined position
        insertAfter.AddAfterSelf(eventCollector);

        Logger.Info($"Added EventCollector definition '{EtwSnapCollectorId}' to WPRP file");
        return true;
    }

    /// <summary>
    /// Creates an EventCollector element (top-level definition without providers)
    /// </summary>
    private static XElement CreateEtwSnapEventCollector(XNamespace ns)
    {
        // Create the EventCollector definition (providers go in the profile reference, not here)
        var eventCollector = new XElement(ns + "EventCollector",
            new XAttribute("Id", EtwSnapCollectorId),
            new XAttribute("Name", "ETWSnap_EventCollector"),
            new XAttribute("HostGuestCorrelation", "true"));

        // Add buffer configuration (10x the minimal settings for headroom)
        // 0.9% of memory (~144 MB on 16GB system) - still lightweight compared to other collectors
        eventCollector.Add(new XElement(ns + "BufferSize",
            new XAttribute("Value", "256")));
        
        eventCollector.Add(new XElement(ns + "Buffers",
            new XAttribute("Value", "0.9"),
            new XAttribute("PercentageOfTotalMemory", "true"),
            new XAttribute("MaximumBufferSpace", "20")));

        return eventCollector;
    }
}
