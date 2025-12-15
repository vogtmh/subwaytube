const { Innertube } = require('youtubei.js');

(async () => {
    try {
        const innertube = await Innertube.create();
        console.log('InnerTube initialized');

        // 1. Verify Trending
        console.log('\n--- Testing Trending ---');
        const trending = await innertube.getTrending();
        if (trending.videos.length > 0) {
            const video = trending.videos[0];
            console.log('Trending Video:', video.title.text);
            console.log('Author:', video.author.name);
            console.log('Thumbnail:', video.thumbnails[0].url);
        } else {
            console.error('No trending videos found');
        }

        // 2. Verify Search
        console.log('\n--- Testing Search ---');
        const search = await innertube.search('test video');
        if (search.results.length > 0) {
            const result = search.results.find(r => r.type === 'Video');
            if (result) {
                console.log('Search Result:', result.title.text);
                console.log('ID:', result.id);
            } else {
                console.error('No video results found');
            }
        } else {
            console.error('No search results found');
        }

        // 3. Verify Video Info (Play)
        console.log('\n--- Testing Video Info ---');
        // Use a known video ID (e.g., from trending or search)
        const videoId = trending.videos[0].id;
        const info = await innertube.getInfo(videoId);
        console.log('Title:', info.basic_info.title);
        console.log('Author:', info.basic_info.author);
        
        try {
            const format = info.chooseFormat({ type: 'video+audio', quality: 'best' });
            console.log('Format found:', format.quality_label, format.container);
            console.log('URL:', format.decipher(innertube.session.player));
        } catch (e) {
            console.error('Failed to get format:', e.message);
        }

        // 4. Verify Channel
        console.log('\n--- Testing Channel ---');
        const channelId = info.basic_info.channel_id;
        const channel = await innertube.getChannel(channelId);
        console.log('Channel:', channel.metadata.title);
        const videos = await channel.getVideos();
        if (videos.videos.length > 0) {
            console.log('Channel Video:', videos.videos[0].title.text);
        } else {
            console.log('No videos found on channel');
        }

    } catch (error) {
        console.error('Verification failed:', error);
    }
})();
