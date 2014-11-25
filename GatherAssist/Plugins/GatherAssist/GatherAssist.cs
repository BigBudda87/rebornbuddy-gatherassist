﻿//-----------------------------------------------------------------------
// <copyright file="GatherAssist.cs" company="Zane McFate">
//      This code file, and this entire plugin, is uncopyrighted.  This means
//       I've put them in the public domain, and released my copyright on all
//       these works.  There is no need to email me for permission -- use my
//       content however you want!  Email it, share it, reprint it with or
//       without credit.  Change it around, break it, and attribute it to me.
//       It's okay.  Attribution is appreciated, but not required.
// </copyright>
// <author>Zane McFate</author>
//-----------------------------------------------------------------------
namespace GatherAssist
{
    using System;
    using System.Collections.Generic;
    using System.Data;
    using System.Diagnostics.CodeAnalysis;
    using System.IO;
    using System.Linq;
    using System.Runtime.InteropServices;
    using System.Threading;
    using System.Timers;
    using System.Windows.Forms;
    using System.Windows.Media;
    using System.Xml.Linq;
    using ff14bot;
    using ff14bot.Enums;
    using ff14bot.Helpers;
    using ff14bot.Interfaces;
    using ff14bot.Managers;
    using ff14bot.NeoProfiles;
    using ff14bot.Settings;
    using Settings;
    using Action = TreeSharp.Action;

    /// <summary>
    /// RebornBuddy plugin for allowing the gathering of multiple counts of multiple items, from various gathering classes.
    /// </summary>
    public class GatherAssist : IBotPlugin
    {
        /// <summary>
        /// The maximum number of gear sets possible in FFXIV.  May need to adjust this as new classes are added.
        /// </summary>
        private const int MaxGearSets = 35;

        /// <summary>
        /// This is a required value for profile building, and does not appear to need much adjusting for gathering profiles, so it is
        ///  static for all profiles generated by this plugin.
        /// </summary>
        private const int KillRadius = 50;

        /// <summary>
        /// Settings for this plugin which should be saved for later use.
        /// </summary>
        private static GatherAssistSettings settings = GatherAssistSettings.Instance;

        /// <summary>
        /// The timer used to periodically check on the gathering status and guide the engine in the proper direction.
        /// </summary>
        private static System.Timers.Timer gatherAssistTimer = new System.Timers.Timer();

        /// <summary>
        /// The list of all current gather requests.  Populated by the plugin entry form and status maintained during plugin execution.
        /// </summary>
        private List<GatherRequest> requestList;

        /// <summary>
        /// The current gather request.  Used for quick reference to current execution parameters.
        /// </summary>
        private GatherRequest currentGatherRequest = null;

        /// <summary>
        /// A table containing all possible maps, organized by aetheryte ID.  Entries should be unique.
        /// </summary>
        [SuppressMessage("StyleCop.CSharp.DocumentationRules", "SA1650:ElementDocumentationMustBeSpelledCorrectly", Justification = "Aetheryte is a FFXIV term.")]
        private DataTable mapsTable;

        /// <summary>
        /// A table containing all items which can be gathered by this plugin.  Includes values necessary to construct and execute
        ///  gathering profiles.
        /// </summary>
        private DataTable itemsTable;

        /// <summary>
        /// The form for user-provided settings.
        /// </summary>
        private GatherAssist_Form form;

        /// <summary>
        /// Gets the author of this plugin.
        /// </summary>
        public string Author
        {
            get { return " Zane McFate"; }
        }

        /// <summary>
        /// Gets the description of the plugin.
        /// </summary>
        public string Description
        {
            get { return "Extends OrderBot gathering functionality to seek multiple items with a single command."; }
        }

        /// <summary>
        /// Gets the current plugin version.
        /// </summary>
        public Version Version
        {
            get { return new Version(0, 3, 4); }
        }

        /// <summary>
        /// Gets the plugin name.
        /// </summary>
        public string Name
        {
            get { return "GatherAssist"; }
        }

        /// <summary>
        /// Gets a value indicating whether we want a settings button.  True because we do want a button.
        /// </summary>
        public bool WantButton
        {
            get { return true; }
        }

        /// <summary>
        /// Gets the text value for the plugin's requisite button.
        /// </summary>
        public string ButtonText
        {
            get { return this.Name + " Settings"; }
        }

        /// <summary>
        /// Gets the color used for log messages which are meant to be visible and important.
        /// </summary>
        private static Color LogMajorColor
        {
            get { return Colors.SkyBlue; }
        }

        /// <summary>
        /// Gets the color used for log message which are less important.  Also for debug messages.
        /// </summary>
        private static Color LogMinorColor
        {
            get { return Colors.Teal; }
        }

        /// <summary>
        /// Gets the color used for log message indicating problems with the plugin.
        /// </summary>
        private static Color LogErrorColor
        {
            get { return Colors.Red; }
        }

        /// <summary>
        /// Handles the IBotPlugin.OnButtonPress event.  Code executed when the user pushes the requisite button for this plugin.
        ///  Initializes the settings form to gather required parameters for the next gathering attempt.
        /// </summary>
        public void OnButtonPress()
        {
            try
            {
                if (this.form == null || this.form.IsDisposed || this.form.Disposing)
                {
                    this.form = new GatherAssist_Form(this.itemsTable);
                }

                this.form.ShowDialog();

                // don't alter anything if the user cancelled the form
                if (this.form.DialogResult == DialogResult.OK)
                {
                    this.InitializeRequestList(this.form.RequestTable); // reinitialize from updated settings
                    gatherAssistTimer.Start();
                    this.ElapseTimer(); // immediately elapse timer to check item counts and set correct profile
                }
            }
            catch (Exception ex)
            {
                this.LogException(ex);
            }
        }

        /// <summary>
        /// Used to compare this plugin to other plugins.  Not currently implemented.
        /// </summary>
        /// <param name="other">The parameter is not used.</param>
        /// <returns>The parameter is not used.</returns>
        public bool Equals(IBotPlugin other)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Handles the IBotPlugin.OnInitialize event.  Initializes data tables, initializes required settings, and prepares the timer for
        ///  future execution.
        /// </summary>
        public void OnInitialize()
        {
            try
            {
                this.InitializeItems();
                this.InitializeMaps();

                if (settings.UpdateIntervalMinutes == 0)
                {
                    settings.UpdateIntervalMinutes = 1;
                }

                if (settings.AutoSkip == null)
                {
                    settings.AutoSkip = false;
                }

                if (settings.AutoSkipInterval == null || settings.AutoSkipInterval < 1)
                {
                    settings.AutoSkipInterval = 1;
                }

                gatherAssistTimer.Elapsed += this.GatherAssistTimer_Elapsed;
                gatherAssistTimer.Interval = settings.UpdateIntervalMinutes * 60000;
            }
            catch (Exception ex)
            {
                this.LogException(ex);
            }
        }

        /// <summary>
        /// Handles the IBotPlugin.OnShutdown event.  Currently does nothing.
        /// </summary>
        public void OnShutdown()
        {
        }

        /// <summary>
        /// Handles the IBotPlugin.OnEnabled event.  Current shows the plugin version, but does nothing else.
        /// </summary>
        public void OnEnabled()
        {
            try
            {
                this.Log(LogMajorColor, " v" + Version.ToString() + " Enabled");
            }
            catch (Exception ex)
            {
                this.LogException(ex);
            }
        }

        /// <summary>
        /// Handles the IBotPlugin.OnDisabled event.  Reports the plugin version and stops iteration of the gather timer.
        /// </summary>
        public void OnDisabled()
        {
            try
            {
                this.Log(LogMajorColor, " v" + Version.ToString() + " Disabled");
                gatherAssistTimer.Stop();
                //// TODO: Assess whether stopping the bot is the best idea here.  Perhaps we should see whether this plugin was executing logic?
                this.BotStop();
            }
            catch (Exception ex)
            {
                this.LogException(ex);
            }
        }

        /// <summary>
        /// Handles the IBotPlugin.OnPulse event.  Currently does nothing.
        /// </summary>
        public void OnPulse()
        {
        }

        /// <summary>
        /// Handles the gatherAssistTimer.Elapsed event.  Runs the ElapseTimer function.
        /// </summary>
        /// <param name="sender">The parameter is not used.</param>
        /// <param name="e">The parameter is not used.</param>
        private void GatherAssistTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            try
            {
                this.ElapseTimer();
            }
            catch (Exception ex)
            {
                this.LogException(ex);
            }
        }

        /// <summary>
        /// Performs periodic actions to monitor and adjust the engine to complete the overall gathering task.  Checks the current gather
        ///  request status and loads the next profile if necessary.  If all gather requests have been fulfilled, moves plugin to finished
        ///  state.
        /// </summary>
        private void ElapseTimer()
        {
            try
            {
                string lastRequest = this.currentGatherRequest == null ? string.Empty : this.currentGatherRequest.ItemName;
                this.UpdateRequestedItemCounts();
                this.ReportGatheringStatus();

                // if no valid gather requests remain, stop the plugin execution
                if (this.currentGatherRequest == null)
                {
                    this.Log(LogMajorColor, "Gather requests complete!  GatherAssist will stop now.");
                    gatherAssistTimer.Stop();
                    this.BotStop();
                    return;
                }
                else if (this.currentGatherRequest.ItemName != lastRequest)
                {
                    // Only load  a profle if the item name has changed; this keeps profile from needlessly reloading.
                    this.LoadProfile();
                }
            }
            catch (Exception ex)
            {
                this.LogException(ex);
            }
        }

        /// <summary>
        /// Converts a supplied request table into the gather request array for profile execution.
        /// </summary>
        /// <param name="requestTable">The data table holding all gather requests.  Should contain the ItemName and Count fields.</param>
        private void InitializeRequestList(DataTable requestTable)
        {
            try
            {
                // TODO: validate parameter requestTable to fit the parameter description.
                this.requestList = new List<GatherRequest>();

                foreach (DataRow dataRow in requestTable.Rows)
                {
                    this.Log(LogMajorColor, "Adding " + dataRow["ItemName"] + " to request list", true);
                    this.requestList.Add(new GatherRequest(Convert.ToString(dataRow["ItemName"]), Convert.ToInt32(dataRow["Count"])));
                }
            }
            catch (Exception ex)
            {
                this.LogException(ex);
            }
        }

        /// <summary>
        /// Updates item counts for all requested items.
        /// </summary>
        private void UpdateRequestedItemCounts()
        {
            try
            {
                this.currentGatherRequest = null; // reset current gather request, will be set to first valid request below

                foreach (GatherRequest curRequest in this.requestList)
                {
                    curRequest.CurrentCount = 0;
                }

                List<InventoryBagId> validBags = new List<InventoryBagId>();
                validBags.Add(InventoryBagId.Bag1);
                validBags.Add(InventoryBagId.Bag2);
                validBags.Add(InventoryBagId.Bag3);
                validBags.Add(InventoryBagId.Bag4);
                validBags.Add(InventoryBagId.Crystals);

                foreach (InventoryBagId curBagId in validBags)
                {
                    this.Log(LogMajorColor, curBagId.ToString(), true);
                    foreach (BagSlot curSlot in InventoryManager.GetBagByInventoryBagId(curBagId))
                    {
                        var obj = this.requestList.FirstOrDefault(x => x.ItemName == curSlot.Name);
                        if (obj != null)
                        {
                            this.Log(LogMajorColor, "Updating count", true);
                            obj.CurrentCount += curSlot.Count;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                this.LogException(ex);
            }
        }

        /// <summary>
        /// Lists the gathering status of all requested items.  Assigns a valid gather request for continuing work.
        /// </summary>
        private void ReportGatheringStatus()
        {
            try
            {
                foreach (GatherRequest curRequest in this.requestList)
                {
                    Color logColor = curRequest.RequestedTotal <= curRequest.CurrentCount ? LogMinorColor : LogMajorColor;
                    this.Log(logColor, string.Format("Item: {0}, Count: {1}, Requested: {2}", curRequest.ItemName, curRequest.CurrentCount, curRequest.RequestedTotal));
                    if (this.currentGatherRequest == null && curRequest.CurrentCount < curRequest.RequestedTotal)
                    {
                        this.Log(LogMajorColor, string.Format("Updating gather request to {0}", curRequest.ItemName), true);
                        this.currentGatherRequest = curRequest;
                    }
                }
            }
            catch (Exception ex)
            {
                this.LogException(ex);
            }
        }

        /// <summary>
        /// Loads a profile to handle the current gather request.
        /// </summary>
        private void LoadProfile()
        {
            try
            {
                bool isValid = true;

                if (this.currentGatherRequest == null)
                {
                    this.Log(LogErrorColor, string.Format("Error: LoadProfile was executed without an active gather request; this should not be done.  Shutting down {0} plugin."));
                    isValid = false;
                }

                this.Log(LogMajorColor, string.Format("Current Gather Request is {0}", this.currentGatherRequest.ItemName), true);
                ItemRecord itemRecord = this.GetItemRecord(this.currentGatherRequest.ItemName);
                if (itemRecord == null)
                {
                    this.Log(LogErrorColor, string.Format("Error: item {0} cannot be located.  A new items entry must be created for this gather request to function properly.", this.currentGatherRequest.ItemName));
                    isValid = false;
                }

                if (!isValid)
                {
                    gatherAssistTimer.Stop();
                    this.BotStop();
                }
                else
                {
                    // stop the bot temporarily to allow for possible class changes.  Also required for a profile load workaround, as the
                    //  bot does not update item names properly during a "live" profile swap.
                    this.BotStop();
                    this.SetClass(itemRecord.ClassName); // switch class if necessary
                    string gatheringSpell = this.GetGatheringSpell(itemRecord.ClassName); // get a gathering spell appropriate for this class
                    // construct profile using the chosen item record
                    string xmlContent = string.Format(
                        "<Profile><Name>{0}</Name><KillRadius>{1}</KillRadius><Order><If Condition=\"not IsOnMap({2}" +
                        ")\"><TeleportTo Name=\"{3}\" AetheryteId=\"{4}\" /></If><Gather while=\"True\"><GatherObject>{5}</GatherObject><HotSpots>" +
                        "<HotSpot Radius=\"{6}\" XYZ=\"{7}\" /></HotSpots><ItemNames><ItemName>{8}</ItemName></ItemNames><GatheringSkillOrder>" +
                        "<GatheringSkill SpellName=\"{9}\" TimesToCast=\"1\" /></GatheringSkillOrder></Gather></Order></Profile>",
                        "Mining: " + itemRecord.ItemName,
                        KillRadius,
                        itemRecord.MapNumber,
                        itemRecord.AetheryteName,
                        itemRecord.AetheryteId,
                        itemRecord.GatherObject,
                        itemRecord.HotspotRadius,
                        itemRecord.Location,
                        itemRecord.ItemName,
                        gatheringSpell);

                    string targetXmlFile = Path.Combine(GlobalSettings.Instance.PluginsPath, "GatherAssist/Temp/gaCurrentProfile.xml");
                    FileInfo profileFile = new FileInfo(targetXmlFile);
                    profileFile.Directory.Create(); // If the directory already exists, this method does nothing.
                    File.WriteAllText(profileFile.FullName, xmlContent);

                    while (ff14bot.Managers.GatheringWindow.WindowOpen)
                    {
                        this.Log(LogMinorColor, "waiting for a window to close...", true);
                        Thread.Sleep(1000);
                    }

                    NeoProfileManager.Load(targetXmlFile, true); // profile will automatically switch to the new gathering profile at this point
                    Thread.Sleep(1000);
                    TreeRoot.Start();
                }
            }
            catch (Exception ex)
            {
                this.LogException(ex);
            }
        }

        /// <summary>
        /// Populates map records for aetheryte teleporting.
        /// </summary>
        [SuppressMessage("StyleCop.CSharp.DocumentationRules", "SA1650:ElementDocumentationMustBeSpelledCorrectly", Justification = "Aetheryte is a FFXIV term.")]
        private void InitializeMaps()
        {
            try
            {
                this.mapsTable = new DataTable("maps");
                this.mapsTable.Columns.Add("AetheryteId");
                this.mapsTable.Columns.Add("AetheryteName");
                this.mapsTable.Columns.Add("MapNumber");

                this.mapsTable.Rows.Add(2, "New Gridania", 132);
                this.mapsTable.Rows.Add(3, "Bentbranch Meadows", 148);
                this.mapsTable.Rows.Add(4, "Hawthorne Hut", 152);
                this.mapsTable.Rows.Add(5, "Quarrymill", 153);
                this.mapsTable.Rows.Add(6, "Camp Tranquil", 153);
                this.mapsTable.Rows.Add(7, "Fallgourd Float", 154);
                this.mapsTable.Rows.Add(8, "Limsa Lominsa", 129);
                this.mapsTable.Rows.Add(9, "Ul'dah", 130);
                this.mapsTable.Rows.Add(10, "Moraby drydocks", 135);
                this.mapsTable.Rows.Add(11, "Costa Del Sol", 137);
                this.mapsTable.Rows.Add(12, "Wineport", 137);
                this.mapsTable.Rows.Add(13, "Swiftperch", 138);
                this.mapsTable.Rows.Add(14, "Aleport", 138);
                this.mapsTable.Rows.Add(15, "Camp Bronze Lake", 139);
                this.mapsTable.Rows.Add(16, "Camp Overlook", 180);
                this.mapsTable.Rows.Add(17, "Horizon", 140);
                this.mapsTable.Rows.Add(18, "Camp Drybone", 145);
                this.mapsTable.Rows.Add(19, "Little Ala Mhigo", 146);
                this.mapsTable.Rows.Add(20, "Forgotten Springs", 146);
                this.mapsTable.Rows.Add(21, "Camp Bluefog", 147);
                this.mapsTable.Rows.Add(22, "Ceruleum Processing Plant", 147);
                this.mapsTable.Rows.Add(23, "Camp Dragonhead", 155);
                this.mapsTable.Rows.Add(24, "Revenant's Toll", 154);
                this.mapsTable.Rows.Add(52, "Summerford Farms", 134);
                this.mapsTable.Rows.Add(53, "Black Brush Station", 141);
                this.mapsTable.Rows.Add(55, "Wolves' Den Pier", 250);
            }
            catch (Exception ex)
            {
                this.LogException(ex);
            }
        }

        /// <summary>
        /// Populates the items table with gatherable items and various required values on where/how to obtain them.
        /// </summary>
        private void InitializeItems()
        {
            try
            {
                this.itemsTable = new DataTable("items");
                this.itemsTable.Columns.Add("ItemName");
                this.itemsTable.Columns.Add("ClassName");
                this.itemsTable.Columns.Add("AetheryteId");
                this.itemsTable.Columns.Add("GatherObject");
                this.itemsTable.Columns.Add("HotspotRadius");
                this.itemsTable.Columns.Add("Location");

                this.itemsTable.Rows.Add("Alumen", "Miner", 18, "Mineral Deposit", 95, "-183.1978, -34.69329, -37.8227");
                this.itemsTable.Rows.Add("Black Alumen", "Miner", 5, "Mineral Deposit", 60, "353.7134, -3.617686, 58.73518");
                this.itemsTable.Rows.Add("Bomb Ash", "Miner", 20, "Rocky Outcrop", 95, "26.02704, 8.851164, 399.923");
                this.itemsTable.Rows.Add("Brown Pigment", "Miner", 10, "Rocky Outcrop", 60, "232.073792, 73.82699, -289.451752");
                this.itemsTable.Rows.Add("Copper Ore", "Miner", 17, "Mineral Deposit", 95, "264.0081,56.19608,206.0519");
                ////this.itemsTable.Rows.Add("Earth Cluster", "Miner", 10, "Rocky Outcrop", 60, "30.000,700.000,40.000");
                this.itemsTable.Rows.Add("Earth Crystal", "Miner", 10, "Rocky Outcrop", 60, "232.073792, 73.82699, -289.451752");
                this.itemsTable.Rows.Add("Earth Shard", "Miner", 10, "Rocky Outcrop", 60, "232.073792, 73.82699, -289.451752");
                this.itemsTable.Rows.Add("Electrum Ore", "Miner", 15, "Mineral Deposit", 95, "425.5676, -2.748671, 180.2855");
                this.itemsTable.Rows.Add("Electrum Sand", "Miner", 15, "Rocky Outcrop", 60, "333.2277, -3.4, 45.06057");
                ////this.itemsTable.Rows.Add("Fire Crystal", "Miner", 18, "Rocky Outcrop", 95, "140.7642, 7.528731, -98.47753"); // not at this location, find a new one
                this.itemsTable.Rows.Add("Fire Shard", "Miner", 17, "Mineral Deposit", 95, "264.0081,56.19608,206.0519");
                this.itemsTable.Rows.Add("Flax", "Botanist", 6, "Lush Vegetation Patch", 80, "-258.2026, -0.427259, 368.3641");
                this.itemsTable.Rows.Add("Grade 2 Carbonized Matter", "Miner", 10, "Rocky Outcrop", 60, "232.073792, 73.82699, -289.451752");
                ////this.itemsTable.Rows.Add("Grade 3 Carbonized Matter", "Miner", 10, "Rocky Outcrop", 60, "21.32569, 43.12733, 717.137"); // walks to location and stands around, investigate
                this.itemsTable.Rows.Add("Ice Shard", "Miner", 5, "Mineral Deposit", 60, "353.7134, -3.617686, 58.73518");
                this.itemsTable.Rows.Add("Iron Ore", "Miner", 17, "Mineral Deposit", 95, "288.9167, 62.34205, -218.6282");
                this.itemsTable.Rows.Add("Lightning Shard", "Miner", 53, "Mineral Deposit", 95, "-123.6678, 3.532623, 221.7551");
                this.itemsTable.Rows.Add("Marble", "Miner", 15, "Rocky Outcrop", 60, "350.000,-3.000,40.000");
                this.itemsTable.Rows.Add("Muddy Water", "Miner", 17, "Mineral Deposit", 95, "264.0081,56.19608,206.0519");
                this.itemsTable.Rows.Add("Mythril Ore", "Miner", 20, "Mineral Deposit", 95, "181.7675, 3.287047, 962.0443");
                this.itemsTable.Rows.Add("Obsidian", "Miner", 17, "Mineral Deposit", 95, "42.69921,56.98661,349.928");
                this.itemsTable.Rows.Add("Raw Fluorite", "Miner", 18, "Mineral Deposit", 95, "-183.1978, -34.69329, -37.8227");
                this.itemsTable.Rows.Add("Raw Heliodor", "Miner", 20, "Mineral Deposit", 95, "181.7675, 3.287047, 962.0443");
                this.itemsTable.Rows.Add("Raw Malachite", "Miner", 18, "Mineral Deposit", 95, "-183.1978, -34.69329, -37.8227");
                this.itemsTable.Rows.Add("Raw Spinel", "Miner", 5, "Mineral Deposit", 60, "353.7134, -3.617686, 58.73518");
                this.itemsTable.Rows.Add("Raw Tourmaline", "Miner", 5, "Mineral Deposit", 60, "353.7134, -3.617686, 58.73518");
                this.itemsTable.Rows.Add("Silex", "Miner", 20, "Rocky Outcrop", 95, "26.02704, 8.851164, 399.923");
                this.itemsTable.Rows.Add("Soiled Femur", "Miner", 17, "Mineral Deposit", 95, "42.69921,56.98661,349.928");
                this.itemsTable.Rows.Add("Tin Ore", "Miner", 17, "Mineral Deposit", 95, "42.69921,56.98661,349.928");
                this.itemsTable.Rows.Add("Water Shard", "Miner", 17, "Mineral Deposit", 95, "264.0081,56.19608,206.0519");
                ////this.itemsTable.Rows.Add("Wind Rock", "Miner", 5, "Rocky Outcrop", 95, "45.63465, 6.407045, 8.635086");
                this.itemsTable.Rows.Add("Wind Shard", "Miner", 53, "Mineral Deposit", 95, "-123.6678, 3.532623, 221.7551");
                ////this.itemsTable.Rows.Add("Wyvern Obsidian", "Miner", 18, "Mineral Deposit", 60, "250.000,5.000,230.000"); // runs into a cliff and runs endlessly, investigate
                this.itemsTable.Rows.Add("Yellow Pigment", "Miner", 10, "Rocky Outcrop", 60, "232.073792, 73.82699, -289.451752");
                this.itemsTable.Rows.Add("Zinc Ore", "Miner", 17, "Mineral Deposit", 95, "42.69921,56.98661,349.928");
            }
            catch (Exception ex)
            {
                this.LogException(ex);
            }
        }

        /// <summary>
        /// Retrieves the full item record for the supplied item name.
        /// </summary>
        /// <param name="itemName">The name of the item being searched.</param>
        /// <returns>The ItemRecord for the supplied item name.  Null if no item name can be found in the item table.</returns>
        private ItemRecord GetItemRecord(string itemName)
        {
            try
            {
                bool isValid = true;
                DataRow[] itemRows = this.itemsTable.Select(string.Format("ItemName = '{0}'", itemName));
                int itemCount = itemRows.Count<DataRow>();
                if (itemCount > 1)
                {
                    this.Log(LogErrorColor, string.Format("CONTACT DEVELOPER! Requested item record {0} exists in {1} records; remove duplicates for this item before continuing.", itemName, itemCount));
                    isValid = false;
                }
                else if (itemCount == 0)
                {
                    this.Log(LogErrorColor, string.Format("CONTACT DEVELOPER! Requested item name {0} does not exist in the item table; plesae create a record for this item before continuing.", itemName));
                    isValid = false;
                }

                if (!isValid)
                {
                    gatherAssistTimer.Stop();
                    this.BotStop();
                }
                else
                {
                    DataRow itemRow = itemRows[0];
                    ItemRecord itemRecord = new ItemRecord();
                    itemRecord.ItemName = Convert.ToString(itemRow["ItemName"]);
                    itemRecord.ClassName = Convert.ToString(itemRow["ClassName"]);
                    itemRecord.AetheryteId = Convert.ToInt32(itemRow["AetheryteId"]);

                    itemRecord.GatherObject = Convert.ToString(itemRow["GatherObject"]);
                    itemRecord.HotspotRadius = Convert.ToInt32(itemRow["HotspotRadius"]);
                    itemRecord.Location = Convert.ToString(itemRow["Location"]);

                    DataRow[] mapRows = this.mapsTable.Select(string.Format("AetheryteId = '{0}'", itemRecord.AetheryteId));
                    int mapCount = mapRows.Count<DataRow>();

                    if (mapCount > 1)
                    {
                        this.Log(LogErrorColor, string.Format("CONTACT DEVELOPER!  Requested Aetheryte ID {0} exists in {1} records; remove duplicates for this aetheryte before continuing.", itemRecord.AetheryteId, mapCount));
                        isValid = false;
                    }
                    else if (mapCount == 0)
                    {
                        this.Log(LogErrorColor, string.Format("CONTACT DEVELOPER!  Requested Aetheryte ID {0} does not exist in the maps table; please create a record for this aetheryte before continuing.", itemRecord.AetheryteId));
                        isValid = false;
                    }

                    if (!isValid)
                    {
                        gatherAssistTimer.Stop();
                        this.BotStop();
                    }
                    else
                    {
                        DataRow mapRow = mapRows[0];
                        itemRecord.AetheryteName = Convert.ToString(mapRow["AetheryteName"]);
                        itemRecord.MapNumber = Convert.ToInt32(mapRow["MapNumber"]);
                        return itemRecord; // return completed itemRow
                    }
                }
            }
            catch (Exception ex)
            {
                this.LogException(ex);
            }

            return null; // if valid ItemRecord was not returned or error was thrown, return null here
        }

        /// <summary>
        /// Safely stops the bot.  Used for "pause" the bot to perform actions which are difficult or impossible to perform
        ///  while a profile is executing.
        /// </summary>
        private void BotStop()
        {
            while (ff14bot.Managers.GatheringWindow.WindowOpen)
            {
                this.Log(LogMinorColor, "waiting for a window to close...", true);
                Thread.Sleep(1000);
            }

            TreeRoot.Stop(); // stop the bot
        }

        /// <summary>
        /// Logs any exceptions encountered during plugin functions.  Stops the plugin timer and the bot.
        /// </summary>
        /// <param name="ex">The exception which should be communicated in the log.</param>
        private void LogException(Exception ex)
        {
            this.Log(LogErrorColor, string.Format("Exception in plugin {0}: {1} {2}", this.Name, ex.Message, ex.StackTrace));
            gatherAssistTimer.Stop();
            this.BotStop();
        }

        /// <summary>
        /// Logs a message from the plugin.  Attaches the plugin name to the message.
        /// </summary>
        /// <param name="color">The color to use in the log.</param>
        /// <param name="message">The message to log.</param>
        private void Log(Color color, string message)
        {
            this.Log(color, message, false);
        }

        /// <summary>
        /// Logs a message from the plugin.  Attaches the plugin name to the message.
        /// </summary>
        /// <param name="color">The color to use in the log.</param>
        /// <param name="message">The message to log.</param>
        /// <param name="debug">When true, appends a DEBUG: tag to the log message.</param>
        private void Log(Color color, string message, bool debug)
        {
            if (debug)
            {
                message = "DEBUG: " + message;
            }

            Logging.Write(color, string.Format("[{0}] {1}", this.Name, message));
        }

        /// <summary>
        /// Changes the current class to the supplied class, if the character is not already that class.
        /// </summary>
        /// <param name="newClass">The class to change to.</param>
        private void SetClass(string newClass)
        {
            try
            {
                if (Core.Me.CurrentJob.ToString() == newClass)
                {
                    this.Log(LogMajorColor, string.Format("Class {0} is already chosen, bypassing SetClass logic", newClass), true);
                    return;
                }

                bool gearSetsUpdated = false;

                // make sure gear sets exist
                if (settings.GearSets == null)
                {
                    this.UpdateGearSets();
                    gearSetsUpdated = true; // make sure gear sets are not updated again in this script
                }

                string newClassString = newClass.ToString();

                int targetGearSet = 0;

                while (true)
                {
                    for (int i = 0; i < settings.GearSets.Length; i++)
                    {
                        if (newClassString == settings.GearSets[i])
                        {
                            targetGearSet = i + 1;
                        }
                    }

                    if (targetGearSet != 0)
                    {
                        ChatManager.SendChat(string.Format("/gs change {0}", targetGearSet));
                        Thread.Sleep(3000); // give the system time to register the class change

                        // if the class change didn't work, update gear sets; assuming the sets have been adjusted
                        if (newClass != Core.Me.CurrentJob.ToString())
                        {
                            this.Log(LogMajorColor, "Gear sets appear to have been adjusted, scanning gear sets for changes...");
                            this.UpdateGearSets();
                            gearSetsUpdated = true;
                        }

                        break;
                    }

                    if (gearSetsUpdated)
                    {
                        throw new ApplicationException(string.Format("No gear set is available for the specified job class {0}; please check your gear sets.", newClassString));
                    }
                    else
                    {
                        // update gear sets, reloop to check again
                        this.UpdateGearSets();
                        gearSetsUpdated = true;
                    }
                }
            }
            catch (Exception ex)
            {
                this.LogException(ex);
            }
        }

        /// <summary>
        /// Updates the list of gear sets for the current character.  Do not overuse, as this requires changing into all gear sets and logging
        ///  the class type of the gear set.
        /// </summary>
        private void UpdateGearSets()
        {
            try
            {
                int maxClasses = 20;
                string[] gearSets = new string[maxClasses];

                for (int i = 0; i < maxClasses; i++)
                {
                    ChatManager.SendChat(string.Format("/gs change {0}", i + 1));
                    Thread.Sleep(3000); // give the system time to register the class change
                    gearSets[i] = Core.Me.CurrentJob.ToString();

                    // if current gear set is the same class type as the previous set, exit loop
                    if (i != 0 && gearSets[i] == gearSets[i - 1])
                    {
                        break;
                    }
                }

                settings.GearSets = gearSets; // save gear sets

                this.Log(LogMajorColor, "Gear sets acquired:");
                for (int i = 0; i < maxClasses; i++)
                {
                    if (gearSets[i] != null)
                    {
                        this.Log(LogMajorColor, string.Format("{0}: {1}", i + 1, gearSets[i]));
                    }
                }
            }
            catch (Exception ex)
            {
                this.LogException(ex);
            }
        }

        /// <summary>
        /// Supplies an appropriate gathering spell for the supplied class.
        /// </summary>
        /// <param name="className">The class name whose spell book should be used.</param>
        /// <returns>A single spell that will work for the specified class name.</returns>
        private string GetGatheringSpell(string className)
        {
            try
            {
                switch (className)
                {
                    case "Miner":
                        return "Sharp Vision II";
                    case "Botanist":
                        return "Leaf Turn I";
                }

                throw new ApplicationException(string.Format("CONTACT DEVELOPER!  Could not determine a gathering spell for class type {0}; please update code.", className));
            }
            catch (Exception ex)
            {
                this.LogException(ex);
                return null;
            }
        }
    }
}
