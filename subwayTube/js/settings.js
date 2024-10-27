function showSettings() {
    tab = 'settings';
    $("#topsettings").hide();
    $(".navbutton").css("background-color", "#111");
    $("#nav_settings").css("background-color", "Highlight");

    var output = `<h2 style="text-align: left;">Settings</h2>
                  <div style="text-align: left; line-height: 150%;">
                    <div>subwayTube ` + appstring + ` by mavodev <br/> powered by <a href="https://invidious.io/" target="_blank">Invidious</a></div>
                    <div id="setting_serverlist"></div>
                    <div id="setting_customserver"><button id="button_customserver" onclick="inputCustomserver()">Set custom server</button></div>
                    <div id="input_customserver">
                        <input type="text" id="text_customserver" name="text_customserver" size="15" />
                        <button id="apply_customserver" onclick="applyCustomserver()">Apply</button>
                        <button id="cancel_customserver" onclick="cancelCustomserver()">Cancel</button>
                    </div>
                    <div id="setting_serverstats"></div>
                    <div style="width: 100%;height:30px"></div>
                    <table style="width:100%;">
                    <tr>
                      <td style="width:33%"><div class="settingsbutton" id="backupbutton" onclick='createBackup()'><img src="images/backup.png" /><br/>Backup</div></td>
                      <td><div class="settingsbutton" id="restorebutton" onclick='restoreBackup()'><img src="images/restore.png" /><br/>Restore</div></td>
                      <td style="width:33%"><div class="settingsbutton" id="clearbutton" onclick='clearHistory()'><img src="images/clean.png" /><br/>Clear history</div></td>
                    </tr>
                    </table>
               
                    <div id="settingstext"></div>
                    <div id="downloadpath">Download folder: <br/>`+ downloadFolder + ` <button onclick="selectDownloadPath()">Change</button></div>
                    <div id="streamquality"></div>
                    <div id="applybutton" style='display:none;' onclick='applySettings()'>Apply</div>
                  </div>`
    if (tab == 'settings') {
        $('#content').html(output);
        //getDownloads();
    }

    getServerlist()
    getStreamquality()
}

function hideSettings() {
    $("#settingsmenu").hide();
}

function applySettings() {
    let servername = $("#servers").val()
    localStorage.invidious_server = 'https://' + servername;
    server = localStorage.invidious_server;
    showServerstats()
    let quality = $("#streamqualityselect").val()
    localStorage.streamquality = quality;
    streamquality = localStorage.streamquality;
    showServerstats()
}

function activateAlternative(alternative) {
    console.log('Server ' + server + ' unavailable, switching to ' + alternative);
    let servername = alternative;
    localStorage.invidious_server = 'https://' + alternative;
    server = localStorage.invidious_server;
    showServerstats()
}

function inputCustomserver() {
    let hostname = server.replace('https://', '');
    $("#text_customserver").val(hostname);
    $("#setting_customserver").hide();
    $("#input_customserver").show();
    $("#text_customserver").focus();
    $('#text_customserver').keydown(function (event) {
        if (event.which === 13) {
            $("#text_customserver").blur()
            applyCustomserver()
        }
    });
}

function applyCustomserver() {
    var customserver = $("#text_customserver").val();
    if (customserver == '') {
        localStorage.use_customserver = false;
        use_customserver = localStorage.use_customserver;
        getServerlist();
    }
    else {
        localStorage.use_customserver = true;
        use_customserver = localStorage.use_customserver;
        activateAlternative(customserver);
    }
    $("#setting_customserver").show();
    $("#input_customserver").hide();
}

function cancelCustomserver() {
    $("#setting_customserver").show();
    $("#input_customserver").hide();
}

function clearSettingstext() {
    $("#settingstext").html('')
}

function getServerlist() {
    let requesturl = 'https://api.invidious.io/instances.json?pretty=1&sort_by=health'

    $.ajax({
        url: requesturl,
        type: 'GET',
        dataType: 'json',
        success(response) {
            serverlist = {}
            var html = `<label for="servers">Serverlist:</label><br/>
                        <select name="servers" id="servers" onchange="applySettings()">`;
            var stats;
            var serveravailable = false;
            var alternativeserver = server;

            for (var i = 0; i < response.length; i++) {
                var element = response[i];
                let servername = element[0]
                let serverurl = 'https://' + servername
                let attributes = element[1]
                let type = attributes.type;
                let cors = attributes.cors;
                let api = attributes.api;

                if (type == 'https' && api == true && cors == true) {
                    try {
                        if (attributes.region) {
                            let region = attributes.region;
                        }
                        if (attributes.monitor) {
                            let uptime = attributes.monitor.uptime
                        }
                        if (server == serverurl) {
                            serveravailable = true;
                            html += '<option value="' + servername + '" selected>' + servername + '</option>'
                        }
                        else {
                            html += '<option value="' + servername + '">' + servername + '</option>'
                        }
                        serverlist[servername] = { "name": servername, "url": serverurl, "attributes": attributes }
                        alternativeserver = servername;
                    }
                    catch (e) {
                        console.log(element);
                        console.log(e.message);
                    }
                }
            }

            html += `</select>`
            if (serveravailable == false && use_customserver == 'false') {
                activateAlternative(alternativeserver);
            }
            if (tab == 'settings') {
                $("#setting_serverlist").html(html);
                let servers_width = $("#servers").width();
                if (servers_width > 120) {
                    $("#button_customserver").width(servers_width);
                    $("#text_customserver").width(servers_width);
                }
                console.log('[Serverlist] updated.')
                showServerstats()
            }
        },
        error(jqXHR, status, errorThrown) {
            console.log('failed to fetch ' + requesturl)
        },
    });
}

function showServerstats() {
    if (tab == 'settings') {
        let hostname = server.replace('https://', '');
        $("#setting_serverstats").html('Currently using: ' + hostname);
    }
    /*
    try {
        let servername = $("#servers").val()
        let attributes = serverlist[servername].attributes
        let region = attributes.region;
        let uptime = attributes.monitor.uptime;
        stats = 'Location: ' + region + ', uptime: ' + uptime;
        if (tab == 'settings') {
            $("#setting_serverstats").html(stats);
        }
    }
    catch (e) {
        $("#setting_serverstats").html('Cannot display serverinfo. Serverlist is unavailable or empty.');
    }*/
}

function getStreamquality() {
    let qualities = ['144p', '360p', '720p', '1080p'];

    let output = `<label for="streamqualityselect">Stream Quality:</label>
                  <select name="streamqualityselect" id="streamqualityselect" onchange="applySettings()">`;
    for (let i = 0; i < qualities.length; i++) {
        let quality = qualities[i];
        let qualitytext = quality + ' (audio issues)';
        if (quality == '360p') {
            qualitytext = quality + ' (recommended)';
        }
        if (quality == streamquality) {
            output += '<option value="' + quality + '" selected>' + qualitytext + '</option>'
        }
        else {
            output += '<option value="' + quality + '">' + qualitytext + '</option>'
        }
    }
    output += '</select>';
    $("#streamquality").html(output);
}

function createBackup() {
    var now = new Date();
    var datestamp = ("0" + now.getDate()).slice(-2) + "-" + ("0" + (now.getMonth() + 1)).slice(-2) + "-" + now.getFullYear();
    var backupJSON = {};
    backupJSON.info = 'subwayTube backup';
    backupJSON.date = datestamp;
    backupJSON.subscriptions = subscriptions;
    backupJSON.playhistory = playhistory;

    var savePicker = new Windows.Storage.Pickers.FileSavePicker();
    savePicker.suggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.Downloads;
    savePicker.fileTypeChoices.insert("JSON", [".json"]);
    savePicker.suggestedFileName = "subwaytube_backup";

    savePicker.pickSaveFileAsync().then(function (file) {
        if (file != null) {
            let jsonstring = JSON.stringify(backupJSON);
            Windows.Storage.FileIO.writeTextAsync(file, jsonstring).done(function () {
                Windows.Storage.CachedFileManager.completeUpdatesAsync(file).done(function (updateStatus) {
                    if (updateStatus === Windows.Storage.Provider.FileUpdateStatus.complete) {
                        console.log("File " + file.name + " was saved.");
                        $("#settingstext").html("File " + file.name + " was saved.");
                        setTimeout(clearSettingstext, 3000)
                    } else {
                        console.log("File " + file.name + " couldn't be saved.");
                        $("#settingstext").html("File " + file.name + " couldn't be saved.");
                        setTimeout(clearSettingstext, 3000)
                    }
                });
            });
        }
        else {
            console.log("Canceled");
        }

    });
}

function selectBackupfile() {
    var picker = new Windows.Storage.Pickers.FileOpenPicker();
    picker.suggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.Downloads;
    picker.fileTypeFilter.append(".json");
    return picker.pickSingleFileAsync();
}

function restoreBackup() {
    selectBackupfile().then(function (file) {
        try {
            Windows.Storage.FileIO.readTextAsync(file).then(function (fileContent) {
                var importedChannels = 0;
                var importedHistory = 0;
                var importArray = JSON.parse(fileContent);

                var fileinfo = importArray.info;
                var filedate = importArray.date;

                if (fileinfo != 'microTube backup' && fileinfo != 'subwayTube backup') {
                    $("#settingstext").html('No valid file. Only subwayTube exports can be restored.');
                    setTimeout(clearSettingstext, 3000)
                    return;
                }

                var filesubs = importArray.subscriptions;

                for (let i = 0; i < filesubs.length; i++) {
                    channel = filesubs[i];
                    authorId = channel.authorId
                    author = channel.author
                    authorThumbnail = channel.image
                    followChannel(authorId, author, authorThumbnail)
                    importedChannels++
                }

                var historybackup = importArray.playhistory;

                for (let i = 0; i < historybackup.length; i++) {
                    historyitem = historybackup[i];
                    id = historyitem.id;
                    name = historyitem.name;
                    image = historyitem.image;
                    authorId = historyitem.authorId
                    author = historyitem.author

                    addHistoryItem(id, name, image, authorId, author);
                    importedHistory++
                }
                let message = 'Restored: ' + importedChannels + ' channels and ' + importedHistory + ' history from ' + filedate;
                $("#settingstext").html(message);
                setTimeout(clearSettingstext, 3000)
            });
        } catch (err) {
            console.error(err);
        }
    });
}

function clearHistory() {
    playhistory = [];
    searchhistory = [];
    localStorage.playhistory = JSON.stringify(playhistory);
    localStorage.searchhistory = JSON.stringify(searchhistory);
    console.log('History has been cleared.')
    $("#settingstext").html('History has been cleared.')
    setTimeout(clearSettingstext, 3000)
}