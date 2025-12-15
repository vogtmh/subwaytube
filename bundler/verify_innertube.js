const { Innertube, UniversalCache } = require('youtubei.js');

(async () => {
    try {
        console.log('Initializing InnerTube...');
        const innertube = await Innertube.create({
            cache: new UniversalCache(false),
            gl: 'US',
            hl: 'en'
        });
        console.log('InnerTube initialized');

        // 1. Verify Home Feed (used in getFeed)
        console.log('\n--- Testing Home Feed (getFeed) ---');
        const trending = await innertube.getHomeFeed();
        if (trending.videos.length > 0) {
            const video = trending.videos[0];
            console.log('✅ Home Feed Video found');
            console.log('   Title:', video.title.text);
            console.log('   Author:', video.author.name);
            console.log('   Thumbnail:', video.thumbnails[0].url);
            console.log('   ID:', video.id);

            // Verify structure matches main.js expectations
            if (!video.title.text || !video.author.name || !video.thumbnails[0].url || !video.id) {
                console.error('❌ Home Feed video structure mismatch!');
            }
        } else {
            console.error('❌ No home feed videos found');
        }

        // 2. Verify Search (used in searchVideos)
        console.log('\n--- Testing Search (searchVideos) ---');
        const search = await innertube.search('test video');
        if (search.results.length > 0) {
            const result = search.results.find(r => r.type === 'Video');
            if (result) {
                console.log('✅ Search Result found');
                console.log('   Title:', result.title.text);
                console.log('   ID:', result.id);
                if (!result.title.text || !result.id || !result.thumbnails[0].url) {
                    console.error('❌ Search result structure mismatch!');
                }
            } else {
                console.warn('⚠️ No video results found in search (only channels/playlists?)');
            }
        } else {
            console.error('❌ No search results found');
        }

        // 3. Verify Video Info (used in playVideo)
        console.log('\n--- Testing Video Info (playVideo) ---');
        // Use a known video ID (e.g., from trending or search)
        const videoId = trending.videos[0]?.id || 'dQw4w9WgXcQ';
        console.log('   Fetching info for:', videoId);
        const info = await innertube.getInfo(videoId);

        if (info.basic_info) {
            console.log('✅ Video Info found');
            console.log('   Title:', info.basic_info.title);
            console.log('   Author:', info.basic_info.author);
            console.log('   Channel ID:', info.basic_info.channel_id);

            // Check streaming data
            try {
                const format = info.chooseFormat({ type: 'video+audio', quality: 'best' });
                if (format) {
                    console.log('✅ Streaming format found');
                    console.log('   URL:', format.decipher(innertube.session.player));
                } else {
                    console.error('❌ No combined format found');
                }
            } catch (e) {
                console.log('   (Combined format not found, trying adaptive)');
                try {
                    const videoFormat = info.chooseFormat({ type: 'video', quality: 'best' });
                    const audioFormat = info.chooseFormat({ type: 'audio' });
                    if (videoFormat && audioFormat) {
                        console.log('✅ Adaptive formats found');
                    } else {
                        console.error('❌ Adaptive formats missing');
                    }
                } catch (e2) {
                    console.error('❌ Failed to find any suitable format:', e2.message);
                }
            }
        } else {
            console.error('❌ Basic info missing');
        }

        // 4. Verify Channel (used in addChannelToFeed)
        console.log('\n--- Testing Channel (addChannelToFeed) ---');
        const channelId = info.basic_info.channel_id;
        console.log('   Fetching channel:', channelId);
        const channel = await innertube.getChannel(channelId);
        console.log('✅ Channel found:', channel.metadata.title);

        const channelVideos = await channel.getVideos();
        if (channelVideos.videos.length > 0) {
            const cVideo = channelVideos.videos[0];
            if (cVideo.type === 'Video') {
                console.log('✅ Channel Video found:', cVideo.title.text);
            }
        } else {
            console.log('⚠️ No videos found on channel');
        }

    } catch (error) {
        console.error('❌ Verification failed:', error);
        process.exit(1);
    }
})();
