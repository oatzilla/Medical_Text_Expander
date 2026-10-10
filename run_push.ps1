$env:PATH = 'C:\Users\GORW01\.cache\codex-runtimes\codex-primary-runtime\dependencies\native\git\cmd;C:\Users\GORW01\.cache\codex-runtimes\codex-primary-runtime\dependencies\native\git\bin;C:\Users\GORW01\.cache\codex-runtimes\codex-primary-runtime\dependencies\native\git\mingw64\bin;' + $env:PATH
Set-Location 'D:\Medical_Text_Expander'
$Host.UI.RawUI.WindowTitle = 'Medical Text Expander - Git Push v1.9.9'
Write-Host ''
Write-Host '========================================================' -ForegroundColor Cyan
Write-Host '   Medical Text Expander - Push Update v1.9.9 to GitHub' -ForegroundColor Green
Write-Host '========================================================' -ForegroundColor Cyan
Write-Host ''
Write-Host '[1/2] Pushing main branch to GitHub...' -ForegroundColor Yellow
Write-Host '(If a GitHub login dialog or browser window appears, please sign in)' -ForegroundColor Gray
Write-Host ''

git push origin main

if ($LASTEXITCODE -eq 0) {
    Write-Host ''
    Write-Host '[OK] Main branch pushed successfully!' -ForegroundColor Green
    Write-Host ''
    Write-Host '[2/2] Deploying gh-pages branch for Web Portal...' -ForegroundColor Yellow
    git fetch origin gh-pages
    $parent = (git rev-parse FETCH_HEAD).Trim()
    $tree = (git write-tree --prefix=docs/).Trim()
    $commit = (git commit-tree $tree -p $parent -m "deploy: update web portal v1.9.9 (otp recovery & supabase migration)").Trim()
    git push origin "${commit}:refs/heads/gh-pages"
    Write-Host ''
    Write-Host '========================================================' -ForegroundColor Green
    Write-Host '   SUCCESS: v1.9.9 is now LIVE on GitHub & GitHub Pages!' -ForegroundColor Green
    Write-Host '   You can now click [Check for Updates] in the App!' -ForegroundColor Yellow
    Write-Host '========================================================' -ForegroundColor Green
} else {
    Write-Host ''
    Write-Host '[!] Push did not complete. Please check the message above.' -ForegroundColor Red
}

Write-Host ''
Read-Host "Press Enter to exit..."